namespace WhiteList.Shared;

public interface IWhiteListRequestManager
{
    const string Identity = "WhiteList.IWhiteListRequestManager";

    /// <summary>Returns the player's whitelist group name from cache, or null if not found.</summary>
    string? GetPlayerGroup(ulong steamId);

    Task AddToWhiteList(ulong steamId, string group, string? playerName = null, string? addedBy = null);

    Task RemoveFromWhiteList(ulong steamId);

    /// <summary>Force re-sync cache from database.</summary>
    void RefreshCache();

    /// <summary>Returns a snapshot of the current cache (steamId -> groupName).</summary>
    IReadOnlyDictionary<ulong, string> GetCacheSnapshot();
}
