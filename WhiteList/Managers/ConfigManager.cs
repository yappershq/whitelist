using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sharp.Shared.Objects;

namespace WhiteList.Managers;

internal interface IConfigManager : IManager
{
    int WhiteListLevel { get; }

    void SetWhiteListLevel(int level);

    bool BroadcastKick { get; }

    string KickMessage { get; }

    string BroadcastMessage { get; }

    IConVar CreateConVar(string name, int defaultValue, string description);

    IConVar CreateConVar(string name, string defaultValue, string description);

    IConVar CreateConVar(string name, bool defaultValue, string description);

    void SyncAllConVars();

    void SaveAllConVars();
}

internal class ConfigManager : IConfigManager
{
    private readonly InterfaceBridge        _bridge;
    private readonly ILogger<ConfigManager> _logger;
    private readonly string                 _configPath;
    private readonly List<IConVar>          _registeredConVars = [];

    private readonly IConVar _level;
    private readonly IConVar _broadcastKick;
    private readonly IConVar _kickMessage;
    private readonly IConVar _broadcastMessage;

    public ConfigManager(InterfaceBridge bridge, ILogger<ConfigManager> logger)
    {
        _bridge     = bridge;
        _logger     = logger;
        _configPath = Path.Combine(Path.GetFullPath(bridge.SharpPath), "configs", "whitelist.cfg");

        _level            = CreateConVar("wl_level",             0,     "Whitelist level: 0=disabled, 1=streamer, 2=+content, 3=+vip, 4=+whitelist");
        _broadcastKick    = CreateConVar("wl_broadcast_kick",    true,  "Broadcast a message in chat when a player is kicked");
        _kickMessage      = CreateConVar("wl_kick_message",      "You are not whitelisted on this server", "Kick reason shown to the player");
        _broadcastMessage = CreateConVar("wl_broadcast_message", "{0} ({1}) has been kicked. Not whitelisted.", "Broadcast message format ({0}=name, {1}=steamid)");
    }

    public bool Init() => true;

    public void OnPostInit(ServiceProvider provider)
    {
        SyncAllConVars();
    }

    public void Shutdown()
    {
        SaveAllConVars();
    }

    public int    WhiteListLevel   => _level.GetInt32();
    public void   SetWhiteListLevel(int level) => _level.SetString(level.ToString());
    public bool   BroadcastKick    => _broadcastKick.GetBool();
    public string KickMessage      => _kickMessage.GetString();
    public string BroadcastMessage => _broadcastMessage.GetString();

    public IConVar CreateConVar(string name, int defaultValue, string description)
    {
        var cvar = _bridge.ConVarManager.CreateConVar(name, defaultValue, description)
                   ?? throw new InvalidOperationException($"Failed to create ConVar: {name}");

        _registeredConVars.Add(cvar);

        return cvar;
    }

    public IConVar CreateConVar(string name, string defaultValue, string description)
    {
        var cvar = _bridge.ConVarManager.CreateConVar(name, defaultValue, description)
                   ?? throw new InvalidOperationException($"Failed to create ConVar: {name}");

        _registeredConVars.Add(cvar);

        return cvar;
    }

    public IConVar CreateConVar(string name, bool defaultValue, string description)
    {
        var cvar = _bridge.ConVarManager.CreateConVar(name, defaultValue, description)
                   ?? throw new InvalidOperationException($"Failed to create ConVar: {name}");

        _registeredConVars.Add(cvar);

        return cvar;
    }

    public void SyncAllConVars()
        => SyncConVars(_registeredConVars);

    public void SaveAllConVars()
        => SaveConVars(_registeredConVars);

    private void SyncConVars(IReadOnlyList<IConVar> convars)
    {
        var configValues = LoadConfigFile();

        if (configValues.Count == 0 && !File.Exists(_configPath))
        {
            WriteConfigFile(convars);

            return;
        }

        foreach (var convar in convars)
        {
            if (configValues.TryGetValue(convar.Name, out var value))
            {
                convar.SetString(value);
            }
        }

        var missingConvars = convars
                             .Where(c => !configValues.ContainsKey(c.Name))
                             .OrderBy(c => c.Name)
                             .ToList();

        if (missingConvars.Count > 0)
        {
            AppendMissingConVars(missingConvars);
        }
    }

    private void SaveConVars(IReadOnlyList<IConVar> convars)
    {
        WriteConfigFile(convars);
    }

    private Dictionary<string, string> LoadConfigFile()
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (!File.Exists(_configPath))
            return values;

        try
        {
            var lines = File.ReadAllLines(_configPath);

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var trimmed = line.Trim();

                if (trimmed.StartsWith("//"))
                {
                    continue;
                }

                var parts = trimmed.Split([' ', '\t'], 2, StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length < 2)
                {
                    continue;
                }

                var key   = parts[0];
                var value = parts[1];

                if (value.Length >= 2 && value.StartsWith('"') && value.EndsWith('"'))
                {
                    value = value[1..^1];
                }

                values[key] = value;
            }

            _logger.LogInformation("Loaded {Count} values from {Path}", values.Count, _configPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load config from {Path}", _configPath);
        }

        return values;
    }

    private void WriteConfigFile(IReadOnlyList<IConVar> convars)
    {
        try
        {
            EnsureDirectoryExists();

            var sb = new StringBuilder();
            sb.AppendLine("// WhiteList Configuration");
            sb.AppendLine("// Auto-generated - edit values as needed");
            sb.AppendLine();

            foreach (var convar in convars)
            {
                sb.AppendLine($"// {convar.HelpString}");
                sb.AppendLine($"{convar.Name} {FormatValue(convar.GetString())}");
                sb.AppendLine();
            }

            File.WriteAllText(_configPath, sb.ToString());
            _logger.LogInformation("Saved config to {Path}", _configPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save config to {Path}", _configPath);
        }
    }

    private void AppendMissingConVars(IReadOnlyList<IConVar> convars)
    {
        try
        {
            var sb = new StringBuilder();

            var existingContent = File.ReadAllText(_configPath);

            if (existingContent.Length > 0 && !existingContent.EndsWith(Environment.NewLine))
            {
                sb.AppendLine();
            }

            sb.AppendLine();
            sb.AppendLine("// --- New Settings Added automatically ---");
            sb.AppendLine();

            foreach (var convar in convars)
            {
                if (!string.IsNullOrEmpty(convar.HelpString))
                {
                    sb.AppendLine($"// {convar.HelpString}");
                }

                sb.AppendLine($"{convar.Name} {FormatValue(convar.GetString())}");
                sb.AppendLine();
            }

            File.AppendAllText(_configPath, sb.ToString());
            _logger.LogInformation("Added {Count} new convars to config", convars.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to append convars to {Path}", _configPath);
        }
    }

    private static string FormatValue(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "\"\"";
        }

        if (value.Any(char.IsWhiteSpace) && !value.StartsWith('"'))
        {
            return $"\"{value}\"";
        }

        return value;
    }

    private void EnsureDirectoryExists()
    {
        var dir = Path.GetDirectoryName(_configPath);

        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }
}
