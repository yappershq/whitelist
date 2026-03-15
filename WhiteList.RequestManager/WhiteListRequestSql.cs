using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Sharp.Shared;
using SqlSugar;
using WhiteList.Shared;

namespace WhiteList.Request.Sql;

public class WhiteListRequestSql : IModSharpModule
{
    private readonly SqlWhiteListManager _sqlManager;

    private readonly ISharedSystem                _sharedSystem;
    private readonly ILogger<WhiteListRequestSql> _logger;

    public WhiteListRequestSql(
        ISharedSystem  sharedSystem,
        string         dllPath,
        string         sharpPath,
        Version        version,
        IConfiguration configuration,
        bool           hotReload)
    {
        _sharedSystem = sharedSystem;

        var loggerFactory = sharedSystem.GetLoggerFactory();
        _logger = loggerFactory.CreateLogger<WhiteListRequestSql>();

        var config           = LoadConfiguration(sharpPath);
        var connectionConfig = BuildConnectionConfig(config, sharpPath);
        var serverId         = config["ServerId"] ?? "0";
        var dataPath         = Path.Combine(sharpPath, "data");
        Directory.CreateDirectory(dataPath);

        _sqlManager = new SqlWhiteListManager(connectionConfig, dataPath, serverId, loggerFactory);
    }

    private static ConnectionConfig BuildConnectionConfig(IConfigurationRoot config, string sharpPath)
    {
        var dbTypeStr = config["Database:Type"]     ?? "MySql";
        var host      = config["Database:Host"]     ?? "localhost";
        var port      = config["Database:Port"]     ?? "3306";
        var database  = config["Database:Database"] ?? "whitelist";
        var user      = config["Database:User"]     ?? "root";
        var password  = config["Database:Password"] ?? "";

        var dbType = dbTypeStr.ToLowerInvariant() switch
        {
            "mysql"      => DbType.MySql,
            "postgresql" => DbType.PostgreSQL,
            "sqlite"     => DbType.Sqlite,
            _            => throw new NotSupportedException(
                                $"Database type '{dbTypeStr}' is not supported. Supported types: mysql, postgresql, sqlite"),
        };

        var connectionString = dbType switch
        {
            DbType.MySql      => $"Server={host};Port={port};Database={database};User={user};Password={password};",
            DbType.PostgreSQL => $"Host={host};Port={port};Database={database};Username={user};Password={password};",
            DbType.Sqlite     => BuildSqliteConnectionString(sharpPath, database),
            _                 => throw new NotSupportedException($"Database type '{dbTypeStr}' is not supported."),
        };

        return new ConnectionConfig
        {
            DbType                = dbType,
            ConnectionString      = connectionString,
            IsAutoCloseConnection = true,
            InitKeyType           = InitKeyType.Attribute,
            MoreSettings = new()
            {
                DisableNvarchar = true,
            },
            LanguageType = LanguageType.English,
        };
    }

    private static IConfigurationRoot LoadConfiguration(string sharpPath)
    {
        var configPath = Path.Combine(Path.GetFullPath(sharpPath), "configs");

        return new ConfigurationBuilder()
               .SetBasePath(configPath)
               .AddJsonFile("whitelist.jsonc", false, false)
               .Build();
    }

    private static string BuildSqliteConnectionString(string sharpPath, string database)
    {
        var dataDir = Path.Combine(sharpPath, "data");
        Directory.CreateDirectory(dataDir);
        var dbPath = Path.Combine(dataDir, $"{database}.db");

        return $"Data Source={dbPath}";
    }

    public bool Init()
    {
        _sqlManager.Init();
        return true;
    }

    public void PostInit()
    {
        _sharedSystem.GetSharpModuleManager()
                     .RegisterSharpModuleInterface<IWhiteListRequestManager>(
                         this, IWhiteListRequestManager.Identity, _sqlManager);
    }

    public void Shutdown()
    {
        _sqlManager.Dispose();
    }

    string IModSharpModule.DisplayName   => "[WhiteList] RequestManager - SQL";
    string IModSharpModule.DisplayAuthor => "Yappers";
}
