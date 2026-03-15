using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using WhiteList.Shared;

namespace WhiteList;

internal class GroupProviderRegistry : IWhiteListGroupRegistry
{
    private readonly ConcurrentDictionary<string, IWhiteListGroupProvider> _providers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<GroupProviderRegistry>                        _logger;

    public GroupProviderRegistry(ILogger<GroupProviderRegistry> logger)
    {
        _logger = logger;
    }

    public void Register(string name, IWhiteListGroupProvider provider)
    {
        _providers[name] = provider;
        _logger.LogInformation("Group provider '{Name}' registered", name);
    }

    public void Unregister(string name)
    {
        if (_providers.TryRemove(name, out _))
            _logger.LogInformation("Group provider '{Name}' unregistered", name);
    }

    internal IEnumerable<IWhiteListGroupProvider> GetProviders() => _providers.Values;
}
