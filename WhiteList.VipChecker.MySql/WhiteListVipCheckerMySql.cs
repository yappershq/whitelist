using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Sharp.Shared;
using WhiteList.Shared;

namespace WhiteList.VipChecker.MySql;

public class WhiteListVipCheckerMySql : IModSharpModule
{
    private readonly ISharedSystem                        _sharedSystem;
    private readonly ILogger<WhiteListVipCheckerMySql>   _logger;
    private readonly MysqlVipChecker?                    _vipChecker;
    private readonly string                              _providerName;

    public WhiteListVipCheckerMySql(
        ISharedSystem  sharedSystem,
        string         dllPath,
        string         sharpPath,
        Version        version,
        IConfiguration configuration,
        bool           hotReload)
    {
        _sharedSystem = sharedSystem;

        var loggerFactory = sharedSystem.GetLoggerFactory();
        _logger = loggerFactory.CreateLogger<WhiteListVipCheckerMySql>();

        var config           = LoadConfiguration(sharpPath);
        var connectionString = BuildConnectionString(config);
        var serverId         = config["ServerId"] ?? "0";
        var groupName        = config["Group"]    ?? "vip";
        _providerName        = groupName;

        if (!string.IsNullOrEmpty(connectionString))
        {
            var dataPath = Path.Combine(sharpPath, "data");
            Directory.CreateDirectory(dataPath);

            _vipChecker = new MysqlVipChecker(connectionString, serverId, groupName, dataPath, loggerFactory);
            _logger.LogInformation("MySQL VIP checker configured, group: '{Group}'", groupName);
        }
        else
        {
            _logger.LogWarning("Database:Host not configured — MySQL VIP checker disabled");
        }
    }

    public bool Init()
    {
        _vipChecker?.Init();
        return true;
    }

    public void PostInit() { }

    public void OnAllModulesLoaded()
    {
        if (_vipChecker is null) return;

        var iface = _sharedSystem.GetSharpModuleManager()
            .GetOptionalSharpModuleInterface<IWhiteListGroupRegistry>(IWhiteListGroupRegistry.Identity);

        if (iface?.Instance is { } registry)
        {
            registry.Register(_providerName, _vipChecker);
        }
        else
        {
            _logger.LogWarning("WhiteList GroupRegistry not found — VIP checker cannot register");
        }
    }

    public void Shutdown()
    {
        _vipChecker?.Dispose();
    }

    private static string? BuildConnectionString(IConfigurationRoot config)
    {
        var host = config["Database:Host"];

        if (string.IsNullOrEmpty(host))
            return null;

        var port     = config["Database:Port"]     ?? "3306";
        var database = config["Database:Database"] ?? "vips";
        var user     = config["Database:User"]     ?? "root";
        var password = config["Database:Password"] ?? "";

        return $"Server={host};Port={port};Database={database};User={user};Password={password};";
    }

    private static IConfigurationRoot LoadConfiguration(string sharpPath)
    {
        var configPath = Path.Combine(Path.GetFullPath(sharpPath), "configs");

        return new ConfigurationBuilder()
               .SetBasePath(configPath)
               .AddJsonFile("whitelist_vip_mysql.jsonc", false, false)
               .Build();
    }

    string IModSharpModule.DisplayName   => "[WhiteList] VipChecker - MySQL";
    string IModSharpModule.DisplayAuthor => "Yappers";
}
