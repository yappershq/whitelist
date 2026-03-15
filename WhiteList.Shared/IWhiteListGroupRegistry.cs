namespace WhiteList.Shared;

/// <summary>
/// Registration interface exposed by the WhiteList plugin as a shared module.
/// External modules use this to register group providers that contribute
/// to a player's effective whitelist priority.
/// </summary>
public interface IWhiteListGroupRegistry
{
    const string Identity = "WhiteList.IWhiteListGroupRegistry";

    void Register(string name, IWhiteListGroupProvider provider);

    void Unregister(string name);
}
