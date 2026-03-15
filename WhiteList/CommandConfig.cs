using Microsoft.Extensions.Configuration;

namespace WhiteList;

internal class SubcommandInfo
{
    public string Permission { get; set; } = string.Empty;
}

internal class CommandInfo
{
    public string                              Permission  { get; set; } = string.Empty;
    public List<string>                        Aliases     { get; set; } = [];
    public Dictionary<string, SubcommandInfo>  Subcommands { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

internal class CommandConfig
{
    public Dictionary<string, CommandInfo> Commands { get; } = new(StringComparer.OrdinalIgnoreCase);

    public static CommandConfig Load(IConfiguration configuration)
    {
        var config = new CommandConfig();

        var section = configuration.GetSection("Commands");

        foreach (var child in section.GetChildren())
        {
            var info = new CommandInfo
            {
                Permission = child["Permission"] ?? string.Empty,
            };

            foreach (var alias in child.GetSection("Aliases").GetChildren())
            {
                if (!string.IsNullOrWhiteSpace(alias.Value))
                    info.Aliases.Add(alias.Value);
            }

            foreach (var sub in child.GetSection("Subcommands").GetChildren())
            {
                info.Subcommands[sub.Key] = new SubcommandInfo
                {
                    Permission = sub["Permission"] ?? string.Empty,
                };
            }

            config.Commands[child.Key] = info;
        }

        config.ApplyDefaults();

        return config;
    }

    public IEnumerable<string> GetAllPermissions()
    {
        foreach (var cmd in Commands.Values)
        {
            if (!string.IsNullOrEmpty(cmd.Permission))
                yield return cmd.Permission;

            foreach (var sub in cmd.Subcommands.Values)
            {
                if (!string.IsNullOrEmpty(sub.Permission))
                    yield return sub.Permission;
            }
        }
    }

    private void ApplyDefaults()
    {
        if (!Commands.TryGetValue("wl", out var wl))
        {
            wl = new CommandInfo { Permission = WhiteListPermissions.Help, Aliases = ["whitelist"] };
            Commands["wl"] = wl;
        }

        wl.Subcommands.TryAdd("add",        new SubcommandInfo { Permission = WhiteListPermissions.Add });
        wl.Subcommands.TryAdd("remove",     new SubcommandInfo { Permission = WhiteListPermissions.Remove });
        wl.Subcommands.TryAdd("tempadd",    new SubcommandInfo { Permission = WhiteListPermissions.TempAdd });
        wl.Subcommands.TryAdd("tempremove", new SubcommandInfo { Permission = WhiteListPermissions.TempRemove });
        wl.Subcommands.TryAdd("tempclear",  new SubcommandInfo { Permission = WhiteListPermissions.TempClear });
        wl.Subcommands.TryAdd("allow",      new SubcommandInfo { Permission = WhiteListPermissions.Level });
        wl.Subcommands.TryAdd("off",        new SubcommandInfo { Permission = WhiteListPermissions.Level });
        wl.Subcommands.TryAdd("check",      new SubcommandInfo { Permission = WhiteListPermissions.Check });
        wl.Subcommands.TryAdd("refresh",    new SubcommandInfo { Permission = WhiteListPermissions.Refresh });
        wl.Subcommands.TryAdd("recent",       new SubcommandInfo { Permission = WhiteListPermissions.Recent });
        wl.Subcommands.TryAdd("removeonline", new SubcommandInfo { Permission = WhiteListPermissions.RemoveOnline });
        wl.Subcommands.TryAdd("help",       new SubcommandInfo { Permission = WhiteListPermissions.Help });
    }
}
