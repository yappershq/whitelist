<div align="center">
  <h1><strong>WhiteList</strong></h1>
  <p>Priority-based server whitelist for ModSharp / CS2 — gate connections by group, with temporary passes, live admin commands, and a pluggable provider API.</p>
</div>

<p align="center">
  <a href="https://github.com/Kxnrl/modsharp-public"><img src="https://img.shields.io/badge/framework-ModSharp-5865F2?logo=github" alt="ModSharp"></a>
  <img src="https://img.shields.io/badge/game-CS2-orange" alt="CS2">
  <img src="https://img.shields.io/github/stars/yappershq/whitelist?style=flat&logo=github" alt="Stars">
</p>

---

WhiteList rejects non-whitelisted players at the connection hook (before they enter the server). Each group has a priority; the active **level** decides which groups may join — `wl allow vip` admits `vip` and everything higher-priority. Whitelist membership comes from a SQL database, optional external providers (e.g. a VIP table), or in-memory temporary passes. The repo ships four projects: the main `WhiteList` module, a SQL-backed `RequestManager`, an optional MySQL `VipChecker` provider, and a `WhiteList.Shared` contract for third-party providers.

## 🚀 Install

Copy the build output into your ModSharp install (`<sharp>` = your `sharp` directory):

| From | To |
|------|----|
| `.build/modules/WhiteList/` | `<sharp>/modules/WhiteList/` |
| `.build/modules/WhiteList.Request.Sql/` | `<sharp>/modules/WhiteList.Request.Sql/` |
| `.build/modules/WhiteList.VipChecker.MySql/` | `<sharp>/modules/WhiteList.VipChecker.MySql/` *(optional)* |
| `.build/shared/WhiteList.Shared/` | `<sharp>/shared/WhiteList.Shared/` |
| `.assets/locales/whitelist.json` | `<sharp>/locales/whitelist.json` |
| `.assets/configs/whitelist.jsonc.example` | `<sharp>/configs/whitelist.jsonc` |
| `.assets/configs/whitelist_vip_mysql.jsonc.example` | `<sharp>/configs/whitelist_vip_mysql.jsonc` *(only with VipChecker)* |

Drop the `.example` suffix and fill in your real database credentials before starting. Restart the server (or change map) to load. `WhiteList` requires the `RequestManager` module for its database lookups; admin commands need **AdminManager**, localized chat needs **LocalizerManager**, and the `recent` / `removeonline` menus need **MenuManager**.

## ⌨️ Commands

All commands are admin-gated through AdminManager. Run `wl` (or `whitelist`) followed by a subcommand. Console always has full access.

| Subcommand | Usage | Description | Permission |
|------------|-------|-------------|------------|
| *(none)* / `help` | `wl` | Show whitelist status + help | `whitelist:help` |
| `add` | `wl add <steamid64> [group]` | Add a player to the permanent whitelist (default group `whitelist`) | `whitelist:add` |
| `remove` | `wl remove <steamid64>` | Remove a player from the permanent whitelist | `whitelist:remove` |
| `tempadd` | `wl tempadd <steamid64>` | Grant a temporary (in-memory) pass | `whitelist:temp:add` |
| `tempremove` | `wl tempremove <steamid64>` | Revoke a temporary pass | `whitelist:temp:remove` |
| `tempclear` | `wl tempclear` | Clear all temporary passes | `whitelist:temp:clear` |
| `allow` | `wl allow <group> \| wl allow all` | Set the active level to a group (and all higher-priority groups), or open to everyone | `whitelist:level` |
| `off` | `wl off` | Disable the whitelist (everyone may join) | `whitelist:level` |
| `check` | `wl check <steamid64>` | Inspect a player's groups, priority, and whether they'd be allowed | `whitelist:check` |
| `refresh` | `wl refresh` | Force re-sync the database cache | `whitelist:refresh` |
| `recent` | `wl recent` | In-game menu of recently rejected players, to whitelist them by group | `whitelist:recent` |
| `removeonline` | `wl removeonline` | In-game menu to remove an online player (permanent or temporary) | `whitelist:removeonline` |

The wildcard `whitelist:*` grants every subcommand. Permissions are configurable per-subcommand in `whitelist.jsonc`.

## ⚙️ Configuration

**`configs/whitelist.cfg`** — runtime ConVars (auto-generated on first run):

| ConVar | Default | Meaning |
|--------|---------|---------|
| `wl_level` | `0` | Active whitelist level. `0` = disabled; otherwise the max group priority allowed to join |
| `wl_broadcast_kick` | `true` | Broadcast a chat message when a player is kicked |
| `wl_kick_message` | `You are not whitelisted on this server` | Kick reason shown to the player |
| `wl_broadcast_message` | `{0} ({1}) has been kicked. Not whitelisted.` | Broadcast format (`{0}`=name, `{1}`=steamid) |

**`configs/whitelist.jsonc`** — structural config loaded at startup:

| Key | Default | Meaning |
|-----|---------|---------|
| `ServerId` | `"0"` | Server identifier for multi-server whitelists. `"0"` = global; global entries are always included |
| `Groups` | `streamer:1, content:2, vip:3, whitelist:4` | Group → priority map (lower number = higher priority) |
| `Commands` | `wl` + `whitelist` alias | Per-command/subcommand permission strings and aliases |
| `Database` | MySql | Connection settings. `Type` supports `MySql`, `PostgreSQL`, `Sqlite` |

**`configs/whitelist_vip_mysql.jsonc`** (only with the VipChecker module): `ServerId`, `Group` (the group VIPs are assigned, must exist in `Groups`), and a MySQL `Database` block that queries the `ms_vips` table.

## 🔧 How it works

Enforcement happens in a pre-connection hook (`HookManager.ConnectClient`), which can reject a client outright with a disconnect reason before they join — `OnClientPreAdminCheck` only blocks the admin check, not the connection. A player is allowed when the whitelist is off (`wl_level 0`), holds a temporary pass, or has a group whose priority is `<= wl_level`. Group membership is resolved from the SQL `RequestManager` cache plus any registered `IWhiteListGroupProvider`s, taking the best (lowest-number) priority. Kicks are deferred to the end of the frame to avoid racing the engine's snapshot send path.

## 🧩 Public API

`WhiteList.Shared` exposes two contracts. External modules register a group provider via `IWhiteListGroupRegistry` (resolve in `OnAllModulesLoaded`):

```csharp
var registry = sharpModuleManager
    .GetOptionalSharpModuleInterface<IWhiteListGroupRegistry>(IWhiteListGroupRegistry.Identity)?.Instance;

registry?.Register("vip", myProvider); // myProvider : IWhiteListGroupProvider
```

`IWhiteListGroupProvider.GetPlayerGroup(ulong steamId)` must return synchronously from an in-memory cache. The SQL `RequestManager` publishes `IWhiteListRequestManager` (add/remove/refresh/snapshot) under the same module-interface pattern. The bundled `WhiteList.VipChecker.MySql` module is a working reference implementation of a provider.

## 📦 Build

```bash
dotnet build -c Release
```

Outputs the three module DLLs under `.build/modules/` and `WhiteList.Shared.dll` under `.build/shared/`.

---

<div align="center">
  <p>Made with ❤️ by <a href="https://github.com/yappershq">yappershq</a></p>
  <p>⭐ Star this repo if you find it useful!</p>
</div>
