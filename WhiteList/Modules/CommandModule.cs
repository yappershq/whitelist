using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using Sharp.Modules.MenuManager.Shared;
using Sharp.Shared.Objects;
using Sharp.Shared.Types;
using WhiteList.Managers;
using static WhiteList.Extensions.ChatExtensions;

namespace WhiteList.Modules;

internal class CommandModule : IModule
{
    private readonly InterfaceBridge        _bridge;
    private readonly IPlayerManager        _playerManager;
    private readonly CommandConfig         _commandConfig;
    private readonly ILogger<CommandModule> _logger;

    private bool _adminCommandsRegistered;

    public CommandModule(InterfaceBridge        bridge,
                         IPlayerManager        playerManager,
                         CommandConfig         commandConfig,
                         ILogger<CommandModule> logger)
    {
        _bridge        = bridge;
        _playerManager = playerManager;
        _commandConfig = commandConfig;
        _logger        = logger;
    }

    public bool Init() => true;

    public void OnAllSharpModulesLoaded()
    {
        RegisterAdminCommands();
    }

    public void Shutdown() { }

    private void RegisterAdminCommands()
    {
        if (_adminCommandsRegistered) return;

        var registry = _bridge.AdminCommandRegistry;
        if (registry is null) return;

        var adminHandlers = new Dictionary<string, Action<IGameClient?, StringCommand>>
        {
            { "wl", HandleWhiteListCommand },
        };

        try
        {
            // Register all concrete permissions for wildcard expansion (whitelist:*)
            var allPermissions = _commandConfig.GetAllPermissions().Distinct().ToImmutableArray();
            if (allPermissions.Length > 0)
                registry.RegisterPermissions(allPermissions);

            foreach (var (commandName, handler) in adminHandlers)
            {
                if (!_commandConfig.Commands.TryGetValue(commandName, out var commandInfo))
                {
                    _logger.LogWarning("Command '{CommandName}' not found in config, skipping", commandName);
                    continue;
                }

                // Collect top-level + all subcommand permissions (OR logic at framework level)
                // Anyone with ANY of these permissions can reach the router;
                // specific subcommand permission is enforced inside the handler.
                var allPerms = new HashSet<string>();

                if (!string.IsNullOrEmpty(commandInfo.Permission))
                    allPerms.Add(commandInfo.Permission);

                foreach (var sub in commandInfo.Subcommands.Values)
                {
                    if (!string.IsNullOrEmpty(sub.Permission))
                        allPerms.Add(sub.Permission);
                }

                var permissions = allPerms.ToImmutableArray();

                registry.RegisterAdminCommand(commandName, handler, permissions);

                foreach (var alias in commandInfo.Aliases)
                    registry.RegisterAdminCommand(alias, handler, permissions);
            }

            _adminCommandsRegistered = true;
            _logger.LogInformation("Admin commands registered via AdminManager");
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to register admin commands");
        }
    }

    private void HandleWhiteListCommand(IGameClient? caller, StringCommand cmd)
    {
        // wl with no args → status + help (requires whitelist:help)
        if (cmd.ArgCount < 1)
        {
            if (!CheckSubcommandPermission(caller, "help")) return;
            HandleStatus(caller);
            return;
        }

        var subcommand = cmd.GetArg(1).ToLower();

        switch (subcommand)
        {
            case "add":       if (CheckSubcommandPermission(caller, "add"))        HandleAdd(caller, cmd);        break;
            case "remove":    if (CheckSubcommandPermission(caller, "remove"))     HandleRemove(caller, cmd);     break;
            case "tempadd":   if (CheckSubcommandPermission(caller, "tempadd"))    HandleTempAdd(caller, cmd);    break;
            case "tempremove":if (CheckSubcommandPermission(caller, "tempremove")) HandleTempRemove(caller, cmd); break;
            case "tempclear": if (CheckSubcommandPermission(caller, "tempclear"))  HandleTempClear(caller);       break;
            case "allow":     if (CheckSubcommandPermission(caller, "allow"))      HandleAllow(caller, cmd);      break;
            case "off":       if (CheckSubcommandPermission(caller, "off"))        HandleOff(caller);             break;
            case "check":     if (CheckSubcommandPermission(caller, "check"))      HandleCheck(caller, cmd);      break;
            case "refresh":   if (CheckSubcommandPermission(caller, "refresh"))    HandleRefresh(caller);         break;
            case "recent":       if (CheckSubcommandPermission(caller, "recent"))       HandleRecent(caller);          break;
            case "removeonline": if (CheckSubcommandPermission(caller, "removeonline")) HandleRemoveOnline(caller);    break;
            case "help":      if (CheckSubcommandPermission(caller, "help"))       HandleHelp(caller);            break;
            default:
                ReplyLocale(caller, "whitelist.command.unknown_subcommand", subcommand);
                break;
        }
    }

    // --- Permission checking ---

    private bool CheckSubcommandPermission(IGameClient? caller, string subcommandName)
    {
        // Console always has access
        if (caller is null)
            return true;

        // Look up the subcommand permission from config
        if (!_commandConfig.Commands.TryGetValue("wl", out var wlCmd))
            return true;

        if (!wlCmd.Subcommands.TryGetValue(subcommandName, out var subInfo))
            return true;

        // Empty permission = no restriction
        if (string.IsNullOrEmpty(subInfo.Permission))
            return true;

        var admin = _bridge.AdminManager?.GetAdmin(caller.SteamId);

        if (admin is not null && admin.HasPermission(subInfo.Permission))
            return true;

        ReplyLocale(caller, "whitelist.command.no_permission", subInfo.Permission);
        return false;
    }

    // --- Subcommand handlers ---

    private void HandleStatus(IGameClient? caller)
    {
        var status = _playerManager.GetStatus();

        ReplyLocale(caller, "whitelist.status.header");

        if (status.CurrentLevel == 0)
        {
            ReplyLocale(caller, "whitelist.status.off");
        }
        else
        {
            var groupLabel = status.CurrentLevelGroupName ?? $"priority {status.CurrentLevel}";
            ReplyLocale(caller, "whitelist.status.on", groupLabel);

            var allowed = status.GroupPriorities
                .Where(kv => kv.Value <= status.CurrentLevel)
                .OrderBy(kv => kv.Value)
                .Select(kv => $"{kv.Key} ({kv.Value})");

            ReplyLocale(caller, "whitelist.status.allowed_groups", string.Join(", ", allowed));
        }

        var groups = string.Join(", ", status.GroupPriorities
            .OrderBy(kv => kv.Value)
            .Select(kv => $"{kv.Key}={kv.Value}"));
        ReplyLocale(caller, "whitelist.status.groups", groups);

        if (status.CachedGroupCounts.Count > 0)
        {
            var counts = status.CachedGroupCounts
                .OrderBy(kv => kv.Key)
                .Select(kv => $"{kv.Key}: {kv.Value}");
            ReplyLocale(caller, "whitelist.status.players", string.Join(", ", counts));
        }

        if (status.TempCount > 0)
            ReplyLocale(caller, "whitelist.status.temp", status.TempCount);

        if (status.ProviderCount > 0)
            ReplyLocale(caller, "whitelist.status.providers", status.ProviderCount);

        HandleHelp(caller);
    }

    private void HandleAdd(IGameClient? caller, StringCommand cmd)
    {
        if (cmd.ArgCount < 2)
        {
            ReplyLiteral(caller, "Usage: wl add <steamid64> [group]");
            return;
        }

        if (!TryParseSteamId(cmd.GetArg(2), out var steamId))
        {
            ReplyLocale(caller, "whitelist.command.invalid_steamid", cmd.GetArg(2));
            return;
        }

        var group = cmd.ArgCount >= 3 ? cmd.GetArg(3) : "whitelist";

        var priorities = _playerManager.GetGroupPriorities();
        if (!priorities.ContainsKey(group))
        {
            var valid = string.Join(", ", priorities.OrderBy(kv => kv.Value).Select(kv => kv.Key));
            ReplyLocale(caller, "whitelist.command.unknown_group", group, valid);
            return;
        }

        var addedBy = caller is not null ? caller.SteamId.ToString() : "console";

        _ = Task.Run(async () =>
        {
            var success = await _playerManager.AddToWhiteList(steamId, group, addedBy: addedBy).ConfigureAwait(false);

            await _bridge.ModSharp.InvokeFrameActionAsync(() =>
            {
                ReplyLocale(caller, success
                    ? "whitelist.command.added"
                    : "whitelist.command.add_failed", steamId, group);
            }).ConfigureAwait(false);
        });
    }

    private void HandleRemove(IGameClient? caller, StringCommand cmd)
    {
        if (cmd.ArgCount < 2)
        {
            ReplyLiteral(caller, "Usage: wl remove <steamid64>");
            return;
        }

        if (!TryParseSteamId(cmd.GetArg(2), out var steamId))
        {
            ReplyLocale(caller, "whitelist.command.invalid_steamid", cmd.GetArg(2));
            return;
        }

        _ = Task.Run(async () =>
        {
            var success = await _playerManager.RemoveFromWhiteList(steamId).ConfigureAwait(false);

            await _bridge.ModSharp.InvokeFrameActionAsync(() =>
            {
                ReplyLocale(caller, success
                    ? "whitelist.command.removed"
                    : "whitelist.command.remove_failed", steamId);
            }).ConfigureAwait(false);
        });
    }

    private void HandleTempAdd(IGameClient? caller, StringCommand cmd)
    {
        if (cmd.ArgCount < 2)
        {
            ReplyLiteral(caller, "Usage: wl tempadd <steamid64>");
            return;
        }

        if (!TryParseSteamId(cmd.GetArg(2), out var steamId))
        {
            ReplyLocale(caller, "whitelist.command.invalid_steamid", cmd.GetArg(2));
            return;
        }

        _playerManager.TempAdd(steamId);
        ReplyLocale(caller, "whitelist.command.tempadded", steamId);
    }

    private void HandleTempRemove(IGameClient? caller, StringCommand cmd)
    {
        if (cmd.ArgCount < 2)
        {
            ReplyLiteral(caller, "Usage: wl tempremove <steamid64>");
            return;
        }

        if (!TryParseSteamId(cmd.GetArg(2), out var steamId))
        {
            ReplyLocale(caller, "whitelist.command.invalid_steamid", cmd.GetArg(2));
            return;
        }

        if (_playerManager.TempRemove(steamId))
            ReplyLocale(caller, "whitelist.command.tempremoved", steamId);
        else
            ReplyLocale(caller, "whitelist.command.tempremove_not_found", steamId);
    }

    private void HandleTempClear(IGameClient? caller)
    {
        var count = _playerManager.TempClear();
        ReplyLocale(caller, "whitelist.command.tempclear", count);
    }

    private void HandleAllow(IGameClient? caller, StringCommand cmd)
    {
        if (cmd.ArgCount < 2)
        {
            ReplyLiteral(caller, "Usage: wl allow <group> | wl allow all");
            return;
        }

        var target = cmd.GetArg(2).ToLower();

        if (target == "all")
        {
            _playerManager.SetLevelAll();
            var priorities = _playerManager.GetGroupPriorities();
            var groups = string.Join(", ", priorities.OrderBy(kv => kv.Value).Select(kv => kv.Key));
            ReplyLocale(caller, "whitelist.command.allow_all", groups);
            return;
        }

        var (success, message) = _playerManager.SetLevelByGroup(target);

        if (success)
            ReplyLocale(caller, "whitelist.command.allow", message);
        else
            ReplyLocale(caller, "whitelist.command.unknown_group", target,
                string.Join(", ", _playerManager.GetGroupPriorities()
                    .OrderBy(kv => kv.Value).Select(kv => kv.Key)));
    }

    private void HandleOff(IGameClient? caller)
    {
        _playerManager.SetLevelOff();
        ReplyLocale(caller, "whitelist.command.off");
    }

    private void HandleCheck(IGameClient? caller, StringCommand cmd)
    {
        if (cmd.ArgCount < 2)
        {
            ReplyLiteral(caller, "Usage: wl check <steamid64>");
            return;
        }

        if (!TryParseSteamId(cmd.GetArg(2), out var steamId))
        {
            ReplyLocale(caller, "whitelist.command.invalid_steamid", cmd.GetArg(2));
            return;
        }

        var result = _playerManager.CheckPlayer(steamId);

        ReplyLocale(caller, "whitelist.check.header", steamId);

        if (result.BestGroup is not null)
            ReplyLocale(caller, "whitelist.check.best_group", result.BestGroup, result.BestPriority);
        else
            ReplyLocale(caller, "whitelist.check.best_group_none");

        if (result.DbGroup is not null)
            ReplyLocale(caller, "whitelist.check.db_group", result.DbGroup, result.DbPriority);

        ReplyLocale(caller, result.IsTempWhitelisted
            ? "whitelist.check.temp_yes"
            : "whitelist.check.temp_no");

        ReplyLocale(caller, "whitelist.check.level", result.CurrentLevel);

        ReplyLocale(caller, result.WouldBeAllowed
            ? "whitelist.check.allowed_yes"
            : "whitelist.check.allowed_no");
    }

    private void HandleRefresh(IGameClient? caller)
    {
        _playerManager.RefreshCache();
        ReplyLocale(caller, "whitelist.command.refresh");
    }

    private void HandleHelp(IGameClient? caller)
    {
        ReplyLocale(caller, "whitelist.help.header");
        ReplyLocale(caller, "whitelist.help.status");
        ReplyLocale(caller, "whitelist.help.add");
        ReplyLocale(caller, "whitelist.help.remove");
        ReplyLocale(caller, "whitelist.help.tempadd");
        ReplyLocale(caller, "whitelist.help.tempremove");
        ReplyLocale(caller, "whitelist.help.tempclear");
        ReplyLocale(caller, "whitelist.help.allow");
        ReplyLocale(caller, "whitelist.help.allow_all");
        ReplyLocale(caller, "whitelist.help.off");
        ReplyLocale(caller, "whitelist.help.check");
        ReplyLocale(caller, "whitelist.help.refresh");
        ReplyLocale(caller, "whitelist.help.recent");
        ReplyLocale(caller, "whitelist.help.removeonline");
    }

    private void HandleRecent(IGameClient? caller)
    {
        if (caller is null)
        {
            ReplyLiteral(caller, "wl recent is only available in-game.");
            return;
        }

        var menuManager = _bridge.MenuManager;
        if (menuManager is null)
        {
            ReplyLocale(caller, "whitelist.recent.no_menu");
            return;
        }

        var rejections = _playerManager.GetRecentRejections();
        if (rejections.Count == 0)
        {
            ReplyLocale(caller, "whitelist.recent.empty");
            return;
        }

        var groups = _playerManager.GetGroupPriorities()
            .OrderBy(kv => kv.Value)
            .Select(kv => kv.Key)
            .ToList();

        var addedBy = caller.SteamId.ToString();

        var menuBuilder = Menu.Create()
            .Title(Loc("whitelist.recent.title"));

        foreach (var rejected in rejections)
        {
            var player = rejected;
            menuBuilder.Item(
                $"{player.Name} ({player.SteamId})",
                controller => OpenGroupPicker(controller, menuManager, player, groups, addedBy));
        }

        menuBuilder.ExitItem();

        menuManager.DisplayMenu(caller, menuBuilder.Build());
    }

    private void OpenGroupPicker(
        IMenuController controller,
        IMenuManager menuManager,
        PlayerManager.RejectedPlayer player,
        List<string> groups,
        string addedBy)
    {
        var groupMenu = Menu.Create()
            .Title(Loc("whitelist.recent.pick_group", player.Name));

        foreach (var group in groups)
        {
            var g = group;
            groupMenu.Item(g, groupController =>
            {
                groupController.Exit();

                _ = Task.Run(async () =>
                {
                    var success = await _playerManager
                        .AddToWhiteList(player.SteamId, g, player.Name, addedBy)
                        .ConfigureAwait(false);

                    await _bridge.ModSharp.InvokeFrameActionAsync(() =>
                    {
                        ReplyLocale(groupController.Client, success
                            ? "whitelist.command.added"
                            : "whitelist.command.add_failed", player.SteamId, g);
                    }).ConfigureAwait(false);
                });
            });
        }

        groupMenu.BackItem();

        controller.Next(groupMenu.Build());
    }

    private void HandleRemoveOnline(IGameClient? caller)
    {
        if (caller is null)
        {
            ReplyLiteral(caller, "wl removeonline is only available in-game.");
            return;
        }

        var menuManager = _bridge.MenuManager;
        if (menuManager is null)
        {
            ReplyLocale(caller, "whitelist.recent.no_menu");
            return;
        }

        var onlinePlayers = _bridge.ClientManager.GetGameClientList(true)
            .Where(p => p is { IsValid: true, IsInGame: true, IsFakeClient: false }
                        && p.SteamId != caller.SteamId)
            .ToList();

        if (onlinePlayers.Count == 0)
        {
            ReplyLocale(caller, "whitelist.removeonline.empty");
            return;
        }

        var menuBuilder = Menu.Create()
            .Title(Loc("whitelist.removeonline.title"));

        foreach (var player in onlinePlayers)
        {
            var p = player;
            menuBuilder.Item(
                $"{p.Name} ({p.SteamId})",
                controller => OpenRemoveTypePicker(controller, p));
        }

        menuBuilder.ExitItem();

        menuManager.DisplayMenu(caller, menuBuilder.Build());
    }

    private void OpenRemoveTypePicker(IMenuController controller, IGameClient target)
    {
        var steamId = target.SteamId;
        var name    = target.Name;

        var typeMenu = Menu.Create()
            .Title(Loc("whitelist.removeonline.pick_type", name))
            .Item(Loc("whitelist.removeonline.permanent"), c =>
            {
                c.Exit();

                _ = Task.Run(async () =>
                {
                    var success = await _playerManager.RemoveFromWhiteList(steamId).ConfigureAwait(false);

                    await _bridge.ModSharp.InvokeFrameActionAsync(() =>
                    {
                        if (success)
                        {
                            ReplyLocale(c.Client, "whitelist.command.removed", steamId);

                            if (_bridge.ClientManager.GetGameClient(steamId) is { } online)
                                _bridge.ClientManager.KickClient(online, "Removed from whitelist");
                        }
                        else
                        {
                            ReplyLocale(c.Client, "whitelist.command.remove_failed", steamId);
                        }
                    }).ConfigureAwait(false);
                });
            })
            .Item(Loc("whitelist.removeonline.temporary"), c =>
            {
                c.Exit();

                _playerManager.TempRemove(steamId);
                ReplyLocale(c.Client, "whitelist.command.tempremoved", steamId);
            })
            .BackItem();

        controller.Next(typeMenu.Build());
    }

    // --- Helpers ---

    private static bool TryParseSteamId(string input, out ulong steamId)
    {
        return ulong.TryParse(input, out steamId) && steamId > 0;
    }
}
