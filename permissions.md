# WhiteList — AdminManager Permissions

Add this to your AdminManager `PermissionCollection` to use `@whitelist` as a group shorthand:

```json
"whitelist": [
  "whitelist:help",
  "whitelist:add",
  "whitelist:remove",
  "whitelist:temp:add",
  "whitelist:temp:remove",
  "whitelist:temp:clear",
  "whitelist:level",
  "whitelist:check",
  "whitelist:refresh",
  "whitelist:recent",
  "whitelist:removeonline"
]
```

Then assign `@whitelist` to any role that should manage the whitelist:

```json
{
  "Name": "admin",
  "Permissions": [
    "@whitelist"
  ]
}
```

Or use the wildcard `whitelist:*` directly in a role's permissions for the same effect.

## Permission Reference

| Permission              | Command(s)          | Description                              |
|-------------------------|---------------------|------------------------------------------|
| `whitelist:help`        | `wl`, `wl help`     | View status and help                     |
| `whitelist:add`         | `wl add`            | Add player to whitelist                  |
| `whitelist:remove`      | `wl remove`         | Remove player from whitelist             |
| `whitelist:temp:add`    | `wl tempadd`        | Temporarily whitelist a player           |
| `whitelist:temp:remove` | `wl tempremove`     | Remove temp whitelist + kick             |
| `whitelist:temp:clear`  | `wl tempclear`      | Clear all temp whitelist entries          |
| `whitelist:level`       | `wl allow`, `wl off`| Set whitelist level or disable           |
| `whitelist:check`       | `wl check`          | Check a player's whitelist status        |
| `whitelist:refresh`     | `wl refresh`        | Force cache reload                       |
| `whitelist:recent`      | `wl recent`         | Menu: whitelist recently rejected players|
| `whitelist:removeonline`| `wl removeonline`   | Menu: remove online player + kick        |
