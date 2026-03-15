using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sharp.Modules.AdminManager.Shared;
using Sharp.Modules.LocalizerManager.Shared;
using Sharp.Modules.MenuManager.Shared;
using Sharp.Shared;
using Sharp.Shared.Managers;
using Sharp.Shared.Objects;
using WhiteList.Shared;

namespace WhiteList;

internal interface IModule
{
    bool Init();

    void OnPostInit(ServiceProvider provider)
    {
    }

    void OnAllSharpModulesLoaded()
    {
    }

    void Shutdown()
    {
    }
}

internal interface IManager
{
    bool Init();

    void OnPostInit(ServiceProvider provider)
    {
    }

    void Shutdown();
}

internal class InterfaceBridge
{
    private readonly ISharedSystem _sharedSystem;

    internal static InterfaceBridge? Instance { get; private set; }

    public InterfaceBridge(string        dllPath,
                           string        sharpPath,
                           Version       version,
                           ISharedSystem sharedSystem,
                           bool          hotReload,
                           bool          debug)
    {
        DllPath       = dllPath;
        SharpPath     = sharpPath;
        Version       = version;
        _sharedSystem = sharedSystem;
        HotReload     = hotReload;
        Debug         = debug;

        ModSharp           = sharedSystem.GetModSharp();
        ConVarManager      = sharedSystem.GetConVarManager();
        ClientManager      = sharedSystem.GetClientManager();
        SharpModuleManager = sharedSystem.GetSharpModuleManager();

        ModuleIdentity = string.IsNullOrEmpty(dllPath)
            ? "WhiteList"
            : Path.GetFileNameWithoutExtension(dllPath);

        Instance = this;
    }

    public string DllPath { get; }

    public string SharpPath { get; }

    public Version Version { get; }

    public bool HotReload { get; }

    public bool Debug { get; }

    public string ModuleIdentity { get; }

    public IModSharp      ModSharp      { get; }
    public IConVarManager ConVarManager { get; }
    public IClientManager ClientManager { get; }

    public ISharpModuleManager SharpModuleManager { get; }

    public ILoggerFactory LoggerFactory => _sharedSystem.GetLoggerFactory();

    // Optional modules — set during OnAllModulesLoaded / OnLibraryConnected
    public IAdminManager?        AdminManager        { get; internal set; }
    public IAdminCommandRegistry? AdminCommandRegistry { get; internal set; }

    private IModSharpModuleInterface<IMenuManager>? _menuManagerInterface;

    public IMenuManager? MenuManager
    {
        get
        {
            if (_menuManagerInterface?.Instance is { } instance)
                return instance;

            _menuManagerInterface = SharpModuleManager
                .GetOptionalSharpModuleInterface<IMenuManager>(IMenuManager.Identity);

            return _menuManagerInterface?.Instance;
        }
    }

    private IWhiteListRequestManager? _cachedRequestManager;
    private ILocalizerManager?        _cachedLocalizerManager;

    public IWhiteListRequestManager GetWhiteListRequestManager()
    {
        if (_cachedRequestManager is not null)
        {
            return _cachedRequestManager;
        }

        var iface = SharpModuleManager.GetRequiredSharpModuleInterface<IWhiteListRequestManager>(
            IWhiteListRequestManager.Identity);

        if (iface is { IsAvailable: true, Instance: { } instance })
        {
            _cachedRequestManager = instance;

            return instance;
        }

        throw new
            InvalidOperationException(
                $"Required module '{IWhiteListRequestManager.Identity}' could not be loaded or is unavailable.");
    }

    public ILocalizerManager GetLocalizerManager()
    {
        if (_cachedLocalizerManager is not null)
        {
            return _cachedLocalizerManager;
        }

        var iface = SharpModuleManager.GetRequiredSharpModuleInterface<ILocalizerManager>(ILocalizerManager.Identity);

        if (iface is { IsAvailable: true, Instance: { } instance })
        {
            _cachedLocalizerManager = instance;

            return instance;
        }

        throw new
            InvalidOperationException(
                $"Required module '{ILocalizerManager.Identity}' could not be loaded or is unavailable.");
    }
}
