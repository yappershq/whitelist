namespace WhiteList.Shared;

/// <summary>
/// Callback interface for external modules that provide group membership.
/// All lookups must be synchronous (from in-memory cache).
/// </summary>
public interface IWhiteListGroupProvider
{
    /// <summary>
    /// Returns the group name this provider assigns to the player from cache, or null if not applicable.
    /// </summary>
    string? GetPlayerGroup(ulong steamId);
}
