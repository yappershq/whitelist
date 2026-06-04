using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Sharp.Shared.Enums;
using Sharp.Shared.Listeners;
using Sharp.Shared.Objects;

namespace WhiteList.Managers;

internal record PlayerCheckResult(
    ulong   SteamId,
    string? DbGroup,
    int?    DbPriority,
    string? BestGroup,
    int?    BestPriority,
    bool    IsTempWhitelisted,
    int     CurrentLevel,
    bool    WouldBeAllowed);

internal record WhiteListStatusInfo(
    int                                  CurrentLevel,
    string?                              CurrentLevelGroupName,
    IReadOnlyDictionary<string, int>     GroupPriorities,
    IReadOnlyDictionary<string, int>     CachedGroupCounts,
    int                                  TempCount,
    int                                  ProviderCount);

internal interface IPlayerManager
{
    Task<bool> AddToWhiteList(ulong steamId, string group, string? playerName = null, string? addedBy = null);
    Task<bool> RemoveFromWhiteList(ulong steamId);

    bool TempAdd(ulong steamId);
    bool TempRemove(ulong steamId);
    int  TempClear();

    (bool Success, string Message) SetLevelByGroup(string groupName);
    void SetLevelAll();
    void SetLevelOff();

    PlayerCheckResult    CheckPlayer(ulong steamId);
    WhiteListStatusInfo  GetStatus();

    IReadOnlyDictionary<string, int> GetGroupPriorities();

    IReadOnlyList<PlayerManager.RejectedPlayer> GetRecentRejections();

    void RefreshCache();
}

internal class PlayerManager : IManager, IClientListener, IPlayerManager
{
    private readonly InterfaceBridge        _bridge;
    private readonly IConfigManager         _configManager;
    private readonly GroupProviderRegistry   _groupRegistry;
    private readonly ILogger<PlayerManager> _logger;

    private readonly Dictionary<string, int>             _groupPriorities;
    private readonly ConcurrentDictionary<ulong, byte>   _tempWhitelist = new();

    internal record RejectedPlayer(string Name, ulong SteamId, DateTime Time);

    private readonly LinkedList<RejectedPlayer> _recentRejections = new();
    private const int MaxRecentRejections = 20;

    public PlayerManager(InterfaceBridge        bridge,
                         IConfigManager         configManager,
                         GroupProviderRegistry   groupRegistry,
                         IConfiguration         configuration,
                         ILogger<PlayerManager> logger)
    {
        _bridge        = bridge;
        _configManager = configManager;
        _groupRegistry = groupRegistry;
        _logger        = logger;

        _groupPriorities = LoadGroupPriorities(configuration);
    }

    public bool Init()
    {
        _bridge.ClientManager.InstallClientListener(this);

        _logger.LogInformation("Loaded {Count} group priorities: {Groups}",
            _groupPriorities.Count,
            string.Join(", ", _groupPriorities.OrderBy(kv => kv.Value).Select(kv => $"{kv.Key}={kv.Value}")));

        return true;
    }

    public void Shutdown()
    {
        _bridge.ClientManager.RemoveClientListener(this);
    }

    // --- Client listener ---

    public bool OnClientPreAdminCheck(IGameClient client)
    {
        if (client.IsFakeClient || client.IsHltv)
            return false;

        var level = _configManager.WhiteListLevel;

        if (level == 0)
            return false;

        if (IsPlayerAllowed(client.SteamId, level))
            return false;

        // NOTE: returning true here only BLOCKS THE ADMIN CHECK (ModSharp IClientListener:
        // "True = Block Check") — it does NOT reject the connection. Non-allowed players must be
        // actively kicked. Defer to the next frame so we don't kick mid listener-dispatch.
        _bridge.ModSharp.InvokeFrameAction(() =>
        {
            if (client.IsValid)
                KickPlayer(client);
        });

        return true;
    }

    public void OnClientPutInServer(IGameClient client) { }

    public void OnClientDisconnected(IGameClient client, NetworkDisconnectionReason reason) { }

    // --- Permanent whitelist ---

    public async Task<bool> AddToWhiteList(ulong steamId, string group, string? playerName = null, string? addedBy = null)
    {
        try
        {
            // Try to resolve player name from connected clients if not provided
            playerName ??= _bridge.ClientManager.GetGameClient(steamId)?.Name;

            var requestManager = _bridge.GetWhiteListRequestManager();
            await requestManager.AddToWhiteList(steamId, group, playerName, addedBy).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add SteamId {SteamId} to group {Group}", steamId, group);
            return false;
        }
    }

    public async Task<bool> RemoveFromWhiteList(ulong steamId)
    {
        try
        {
            var requestManager = _bridge.GetWhiteListRequestManager();
            await requestManager.RemoveFromWhiteList(steamId).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove SteamId {SteamId} from whitelist", steamId);
            return false;
        }
    }

    // --- Temporary whitelist ---

    public bool TempAdd(ulong steamId)
    {
        _tempWhitelist[steamId] = 0;
        _logger.LogInformation("Temp-added SteamId {SteamId}", steamId);
        return true;
    }

    public bool TempRemove(ulong steamId)
    {
        if (!_tempWhitelist.TryRemove(steamId, out _))
            return false;

        _logger.LogInformation("Temp-removed SteamId {SteamId}", steamId);

        if (_bridge.ClientManager.GetGameClient(steamId) is { } client)
        {
            KickPlayer(client);
        }

        return true;
    }

    public int TempClear()
    {
        var keys  = _tempWhitelist.Keys.ToList();
        var count = keys.Count;

        _tempWhitelist.Clear();

        foreach (var steamId in keys)
        {
            if (_bridge.ClientManager.GetGameClient(steamId) is { } client)
            {
                if (!IsPlayerAllowedWithoutTemp(client.SteamId, _configManager.WhiteListLevel))
                {
                    KickPlayer(client);
                }
            }
        }

        _logger.LogInformation("Temp whitelist cleared, {Count} entries removed", count);
        return count;
    }

    // --- Level management ---

    public (bool Success, string Message) SetLevelByGroup(string groupName)
    {
        if (_groupPriorities.TryGetValue(groupName, out var priority))
        {
            _configManager.SetWhiteListLevel(priority);

            var allowed = _groupPriorities
                .Where(kv => kv.Value <= priority)
                .OrderBy(kv => kv.Value)
                .Select(kv => kv.Key);

            return (true, $"Level set to '{groupName}' (priority {priority}). Allowing: {string.Join(", ", allowed)}");
        }

        var valid = string.Join(", ", _groupPriorities.OrderBy(kv => kv.Value).Select(kv => kv.Key));
        return (false, $"Unknown group '{groupName}'. Valid groups: {valid}");
    }

    public void SetLevelAll()
    {
        var max = _groupPriorities.Count > 0 ? _groupPriorities.Values.Max() : 0;
        _configManager.SetWhiteListLevel(max);
    }

    public void SetLevelOff()
    {
        _configManager.SetWhiteListLevel(0);
    }

    // --- Introspection ---

    public PlayerCheckResult CheckPlayer(ulong steamId)
    {
        var requestManager = _bridge.GetWhiteListRequestManager();

        var dbGroup    = requestManager.GetPlayerGroup(steamId);
        int? dbPriority = dbGroup is not null && _groupPriorities.TryGetValue(dbGroup, out var dbp) ? dbp : null;

        var bestGroup    = dbGroup;
        var bestPriority = dbPriority;

        foreach (var provider in _groupRegistry.GetProviders())
        {
            var group = provider.GetPlayerGroup(steamId);

            if (group is not null && _groupPriorities.TryGetValue(group, out var prio))
            {
                if (bestPriority is null || prio < bestPriority)
                {
                    bestGroup    = group;
                    bestPriority = prio;
                }
            }
        }

        var isTempWhitelisted = _tempWhitelist.ContainsKey(steamId);
        var level             = _configManager.WhiteListLevel;

        var wouldBeAllowed = level == 0
                             || isTempWhitelisted
                             || (bestPriority is not null && bestPriority <= level);

        return new PlayerCheckResult(
            steamId, dbGroup, dbPriority,
            bestGroup, bestPriority,
            isTempWhitelisted, level, wouldBeAllowed);
    }

    public WhiteListStatusInfo GetStatus()
    {
        var level = _configManager.WhiteListLevel;

        string? levelGroupName = null;

        if (level > 0)
        {
            levelGroupName = _groupPriorities
                .Where(kv => kv.Value == level)
                .Select(kv => kv.Key)
                .FirstOrDefault();
        }

        var cachedGroupCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var snapshot = _bridge.GetWhiteListRequestManager().GetCacheSnapshot();

            foreach (var kv in snapshot)
            {
                cachedGroupCounts.TryGetValue(kv.Value, out var count);
                cachedGroupCounts[kv.Value] = count + 1;
            }
        }
        catch
        {
            // RequestManager might not be available yet
        }

        return new WhiteListStatusInfo(
            level,
            levelGroupName,
            _groupPriorities,
            cachedGroupCounts,
            _tempWhitelist.Count,
            _groupRegistry.GetProviders().Count());
    }

    public IReadOnlyDictionary<string, int> GetGroupPriorities() => _groupPriorities;

    public IReadOnlyList<RejectedPlayer> GetRecentRejections()
    {
        lock (_recentRejections)
        {
            return _recentRejections.ToList();
        }
    }

    public void RefreshCache()
    {
        try
        {
            _bridge.GetWhiteListRequestManager().RefreshCache();
            _logger.LogInformation("Cache refresh triggered");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh cache");
        }
    }

    // --- Internal ---

    private bool IsPlayerAllowed(ulong steamId, int level)
    {
        if (_tempWhitelist.ContainsKey(steamId))
            return true;

        return IsPlayerAllowedWithoutTemp(steamId, level);
    }

    private bool IsPlayerAllowedWithoutTemp(ulong steamId, int level)
    {
        if (level == 0)
            return true;

        var requestManager = _bridge.GetWhiteListRequestManager();

        int? bestPriority = null;

        var dbGroup = requestManager.GetPlayerGroup(steamId);

        if (dbGroup is not null && _groupPriorities.TryGetValue(dbGroup, out var dbPriority))
        {
            bestPriority = dbPriority;
        }

        foreach (var provider in _groupRegistry.GetProviders())
        {
            var group = provider.GetPlayerGroup(steamId);

            if (group is not null && _groupPriorities.TryGetValue(group, out var priority))
            {
                if (bestPriority is null || priority < bestPriority)
                {
                    bestPriority = priority;
                }
            }
        }

        return bestPriority is not null && bestPriority <= level;
    }

    private void KickPlayer(IGameClient client)
    {
        var name    = client.Name;
        var steamId = client.SteamId;

        RecordRejection(name, steamId);

        if (_configManager.BroadcastKick)
        {
            Extensions.ChatExtensions.PrintLocaleAll("whitelist.kick.broadcast", name, steamId);
        }

        _logger.LogInformation("Rejecting player {Name} ({SteamId}) - not allowed at level {Level}",
            name, steamId, _configManager.WhiteListLevel);

        _bridge.ClientManager.KickClient(client, _configManager.KickMessage);
    }

    private void RecordRejection(string name, ulong steamId)
    {
        lock (_recentRejections)
        {
            // Remove existing entry for same steamId to avoid duplicates
            var node = _recentRejections.First;
            while (node is not null)
            {
                var next = node.Next;
                if (node.Value.SteamId == steamId)
                    _recentRejections.Remove(node);
                node = next;
            }

            _recentRejections.AddFirst(new RejectedPlayer(name, steamId, DateTime.UtcNow));

            while (_recentRejections.Count > MaxRecentRejections)
                _recentRejections.RemoveLast();
        }
    }

    private static Dictionary<string, int> LoadGroupPriorities(IConfiguration configuration)
    {
        var priorities = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        var groupsSection = configuration.GetSection("Groups");

        foreach (var child in groupsSection.GetChildren())
        {
            if (int.TryParse(child.Value, out var priority))
            {
                priorities[child.Key] = priority;
            }
        }

        if (priorities.Count == 0)
        {
            priorities["streamer"]  = 1;
            priorities["content"]   = 2;
            priorities["vip"]       = 3;
            priorities["whitelist"] = 4;
        }

        return priorities;
    }

    public int ListenerVersion  => IClientListener.ApiVersion;
    public int ListenerPriority => 1;
}
