using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sharp.Modules.AdminManager.Shared;
using Sharp.Shared;
using Sharp.Shared.Abstractions;
using WhiteList.Shared;

namespace WhiteList;

public class WhiteList : IModSharpModule
{
    private readonly ISharedSystem       _shared;
    private readonly ServiceProvider     _serviceProvider;
    private readonly ILogger<WhiteList>  _logger;
    private readonly InterfaceBridge     _bridge;
    private readonly CommandConfig       _commandConfig;
    private bool _adminMounted;

    public WhiteList(
        ISharedSystem  sharedSystem,
        string         dllPath,
        string         sharpPath,
        Version        version,
        IConfiguration configuration,
        bool           hotReload)
    {
        _shared = sharedSystem;
        var loggerFactory = sharedSystem.GetLoggerFactory();
        _logger = loggerFactory.CreateLogger<WhiteList>();

        var bridge = new InterfaceBridge(dllPath,
                                         sharpPath,
                                         version,
                                         sharedSystem,
                                         hotReload,
                                         sharedSystem.GetModSharp()
                                                     .HasCommandLine("-debug"));

        var services = new ServiceCollection();

        var moduleConfiguration = LoadConfiguration(sharpPath);

        services.AddSingleton(bridge);
        services.AddSingleton(loggerFactory);
        services.AddSingleton(sharedSystem);
        services.AddSingleton<IConfiguration>(moduleConfiguration);
        var commandConfig = CommandConfig.Load(moduleConfiguration);
        services.AddSingleton(commandConfig);
        services.AddLogging();

        services.AddSingleton<GroupProviderRegistry>();

        services.AddManagerDi();
        services.AddModuleDi();

        _bridge        = bridge;
        _commandConfig = commandConfig;

        _serviceProvider = services.BuildServiceProvider();
    }

    public bool Init()
    {
        foreach (var service in _serviceProvider.GetServices<IManager>())
        {
            try
            {
                if (service.Init())
                {
                    if (_bridge.Debug)
                    {
                        _logger.LogInformation("{service} Initialized", service.GetType().FullName);
                    }

                    continue;
                }

                _logger.LogError("Failed to init {service}!", service.GetType().FullName);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to init {service}!", service.GetType().FullName);
            }

            return false;
        }

        foreach (var service in _serviceProvider.GetServices<IModule>())
        {
            try
            {
                if (service.Init())
                {
                    if (_bridge.Debug)
                    {
                        _logger.LogInformation("{service} Initialized", service.GetType().FullName);
                    }

                    continue;
                }

                _logger.LogError("Failed to init {service}!", service.GetType().FullName);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to init {service}!", service.GetType().FullName);
            }

            return false;
        }

        return true;
    }

    public void PostInit()
    {
        foreach (var service in _serviceProvider.GetServices<IManager>())
        {
            try
            {
                service.OnPostInit(_serviceProvider);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error when calling PostInit for {service}", service.GetType().FullName);
            }
        }

        foreach (var service in _serviceProvider.GetServices<IModule>())
        {
            try
            {
                service.OnPostInit(_serviceProvider);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error when calling PostInit for {service}", service.GetType().FullName);
            }
        }

        var registry = _serviceProvider.GetRequiredService<GroupProviderRegistry>();
        _shared.GetSharpModuleManager()
               .RegisterSharpModuleInterface<IWhiteListGroupRegistry>(
                   this, IWhiteListGroupRegistry.Identity, registry);
    }

    public void Shutdown()
    {
        foreach (var service in _serviceProvider.GetServices<IManager>())
        {
            try
            {
                service.Shutdown();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error when calling Shutdown for {service}", service.GetType().FullName);
            }
        }

        foreach (var service in _serviceProvider.GetServices<IModule>())
        {
            try
            {
                service.Shutdown();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error when calling Shutdown for {service}", service.GetType().FullName);
            }
        }
    }

    public void OnAllModulesLoaded()
    {
        _bridge.GetLocalizerManager().LoadLocaleFile("whitelist", true);

        MountAdminManifest(logFailure: true);

        foreach (var service in _serviceProvider.GetServices<IModule>())
        {
            try
            {
                service.OnAllSharpModulesLoaded();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error when calling OnAllSharpModulesLoaded for {service}",
                    service.GetType().FullName);
            }
        }
    }

    public void OnLibraryConnected(string name)
    {
        if (name.Equals(IAdminManager.Identity, StringComparison.OrdinalIgnoreCase))
        {
            MountAdminManifest();
        }
    }

    public void OnLibraryDisconnect(string name)
    {
        if (name.Equals(IAdminManager.Identity, StringComparison.OrdinalIgnoreCase))
        {
            _bridge.AdminManager         = null;
            _bridge.AdminCommandRegistry = null;
            _adminMounted                = false;
        }
    }

    private void MountAdminManifest(bool logFailure = false)
    {
        if (_adminMounted) return;

        try
        {
            var adminInterface = _bridge.SharpModuleManager
                .GetOptionalSharpModuleInterface<IAdminManager>(IAdminManager.Identity);

            if (adminInterface is not { Instance: { } admin })
            {
                if (logFailure)
                    _logger.LogWarning(
                        "AdminManager not found — admin commands unavailable. Is '{Name}' installed?",
                        IAdminManager.Identity);
                return;
            }

            var permissions = new HashSet<string>(_commandConfig.GetAllPermissions());

            admin.MountAdminManifest(
                _bridge.ModuleIdentity,
                () => new AdminTableManifest(
                    new Dictionary<string, HashSet<string>>
                    {
                        ["whitelist"] = permissions,
                    },
                    [],
                    []
                )
            );

            _bridge.AdminManager         = admin;
            _bridge.AdminCommandRegistry = admin.GetCommandRegistry(_bridge.ModuleIdentity);
            _adminMounted                = true;

            _logger.LogInformation("WhiteList admin manifest mounted and command registry acquired");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to mount admin manifest");
        }
    }

    private static IConfigurationRoot LoadConfiguration(string sharpPath)
    {
        var configPath = Path.Combine(Path.GetFullPath(sharpPath), "configs");

        return new ConfigurationBuilder()
               .SetBasePath(configPath)
               .AddJsonFile("whitelist.jsonc", false, false)
               .Build();
    }

    string IModSharpModule.DisplayName   => "WhiteList";
    string IModSharpModule.DisplayAuthor => "Yappers";
}
