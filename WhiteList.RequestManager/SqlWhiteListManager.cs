using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SqlSugar;
using WhiteList.Shared;

namespace WhiteList.Request.Sql;

public class SqlWhiteListManager : IWhiteListRequestManager, IDisposable
{
    private const string GlobalServerId = "0";

    private readonly SqlSugarScope                       _db;
    private readonly ILogger<SqlWhiteListManager>        _logger;
    private readonly ConcurrentDictionary<ulong, string> _cache = new();
    private readonly string                              _cacheFilePath;
    private readonly string                              _serverId;

    private Timer? _syncTimer;

    public SqlWhiteListManager(ConnectionConfig connectionConfig,
                               string          dataPath,
                               string          serverId,
                               ILoggerFactory  loggerFactory)
    {
        _logger        = loggerFactory.CreateLogger<SqlWhiteListManager>();
        _db            = new SqlSugarScope(connectionConfig);
        _cacheFilePath = Path.Combine(dataPath, "whitelist_cache.json");
        _serverId      = serverId;
    }

    public void Init()
    {
        _db.CodeFirst.InitTables<WhiteListEntity>();

        LoadCacheFromFile();
        SyncFromDatabase();

        _syncTimer = new Timer(_ => SyncFromDatabase(), null,
            TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));

        _logger.LogInformation("SqlWhiteListManager initialized for server '{ServerId}' with {Count} cached entries",
            _serverId, _cache.Count);
    }

    public string? GetPlayerGroup(ulong steamId)
    {
        return _cache.TryGetValue(steamId, out var group) ? group : null;
    }

    public async Task AddToWhiteList(ulong steamId, string group, string? playerName = null, string? addedBy = null)
    {
        try
        {
            // Remove any existing entry for this player on this server
            await _db.Deleteable<WhiteListEntity>()
                     .Where(x => x.SteamId == steamId && x.ServerId == _serverId)
                     .ExecuteCommandAsync();

            var entity = new WhiteListEntity
            {
                ServerId   = _serverId,
                SteamId    = steamId,
                PlayerName = playerName,
                GroupName  = group,
                AddedAt    = DateTime.UtcNow,
                AddedBy    = addedBy,
            };

            await _db.Insertable(entity).ExecuteCommandAsync();

            _cache[steamId] = group;
            SaveCacheToFile();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add SteamId {SteamId} to group {Group}", steamId, group);
            throw;
        }
    }

    public async Task RemoveFromWhiteList(ulong steamId)
    {
        try
        {
            await _db.Deleteable<WhiteListEntity>()
                     .Where(x => x.SteamId == steamId && x.ServerId == _serverId)
                     .ExecuteCommandAsync();

            _cache.TryRemove(steamId, out _);
            SaveCacheToFile();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove SteamId {SteamId} from whitelist", steamId);
            throw;
        }
    }

    private void SyncFromDatabase()
    {
        try
        {
            // Count entries for this server + global
            var count = _db.Queryable<WhiteListEntity>()
                           .Where(x => x.ServerId == _serverId || x.ServerId == GlobalServerId)
                           .Count();

            if (count == _cache.Count) return;

            var entities = _db.Queryable<WhiteListEntity>()
                              .Where(x => x.ServerId == _serverId || x.ServerId == GlobalServerId)
                              .ToList();

            _cache.Clear();

            foreach (var e in entities)
            {
                // Server-specific entry takes priority over global
                if (_cache.TryGetValue(e.SteamId, out _) && e.ServerId == GlobalServerId)
                    continue;

                _cache[e.SteamId] = e.GroupName;
            }

            SaveCacheToFile();
            _logger.LogInformation("Whitelist cache synced from database: {Count} entries", _cache.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync whitelist cache from database");
        }
    }

    private void LoadCacheFromFile()
    {
        if (!File.Exists(_cacheFilePath)) return;

        try
        {
            var json = File.ReadAllText(_cacheFilePath);
            var data = JsonSerializer.Deserialize<Dictionary<string, string>>(json);

            if (data is null) return;

            foreach (var kv in data)
            {
                if (ulong.TryParse(kv.Key, out var steamId))
                {
                    _cache[steamId] = kv.Value;
                }
            }

            _logger.LogInformation("Loaded {Count} entries from whitelist cache file", _cache.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load whitelist cache from {Path}", _cacheFilePath);
        }
    }

    private void SaveCacheToFile()
    {
        try
        {
            var dir = Path.GetDirectoryName(_cacheFilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var data = _cache.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value);
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_cacheFilePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save whitelist cache to {Path}", _cacheFilePath);
        }
    }

    public void RefreshCache()
    {
        SyncFromDatabase();
    }

    public IReadOnlyDictionary<ulong, string> GetCacheSnapshot()
    {
        return new Dictionary<ulong, string>(_cache);
    }

    public void Dispose()
    {
        _syncTimer?.Dispose();
        _db.Dispose();
    }
}
