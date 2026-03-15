# WhiteList Web UI Integration Guide

This document describes the CS2 whitelist system from a web developer's perspective. The game servers and the web UI share the same MySQL database. The game server polls the database every 30 seconds — there is no API to call, no websocket, no webhook. You write to the database, the game server picks it up.

## Database

### Table: `whitelist`

```sql
CREATE TABLE whitelist (
    Id          INT          AUTO_INCREMENT PRIMARY KEY,
    ServerId    VARCHAR(32)  NOT NULL DEFAULT '0',
    SteamId     BIGINT UNSIGNED NOT NULL,
    PlayerName  VARCHAR(64)  NULL,
    GroupName   VARCHAR(32)  NOT NULL DEFAULT 'whitelist',
    AddedAt     DATETIME     NOT NULL,
    AddedBy     VARCHAR(64)  NULL
);
```

| Column | Type | Description |
|---|---|---|
| `Id` | int, auto-increment | Unique row identifier for CRUD operations |
| `ServerId` | varchar(32) | Which server this entry belongs to. `"0"` = global (applies to ALL servers) |
| `SteamId` | bigint unsigned | Player's Steam ID 64 (e.g. `76561198012345678`) |
| `PlayerName` | varchar(64), nullable | Last known player name. Display-only, not used for logic. Set it when you can for better UX |
| `GroupName` | varchar(32) | The whitelist group (see Group Priority System below) |
| `AddedAt` | datetime | When the entry was created (UTC) |
| `AddedBy` | varchar(64), nullable | Who added this entry. Could be a SteamID64 (admin from game), `"console"`, or `"web"`, or a username from your auth system |

### Unique constraint

There should be one entry per `(ServerId, SteamId)` pair. If a player is moved to a different group on the same server, the old row is deleted and a new one is inserted. The game server handles this automatically. Your web UI should enforce the same: when adding a player who already exists on that server, update/replace the row.

### Table: `ms_vips` (separate database, optional)

VIPs come from a completely separate system (separate database, separate module). The whitelist module has zero knowledge of VIPs — it discovers them at runtime via a plugin registration system. Your web UI does NOT need to manage VIPs through the whitelist table. If you need to manage VIPs, that's a separate database and separate UI.

## Group Priority System

Groups have numeric priorities. Lower number = higher priority = more exclusive access.

```
streamer  = 1  (highest priority, most exclusive)
content   = 2
vip       = 3
whitelist = 4  (lowest priority, least exclusive)
```

These are configured in `whitelist.jsonc` on each game server. The defaults above are sensible and most deployments won't change them.

### How the game server uses groups

The game server has a "whitelist level" (an integer, 0-4 by default):
- Level `0` = whitelist is OFF, everyone can join
- Level `1` = only `streamer` (priority ≤ 1) can join
- Level `2` = `streamer` + `content` (priority ≤ 2) can join
- Level `3` = `streamer` + `content` + `vip` (priority ≤ 3) can join
- Level `4` = all whitelisted players can join

Admins control this in-game with commands like `wl allow vip` (sets level to 3) or `wl off` (sets level to 0). The web UI does NOT need to control the whitelist level — that's a live server operation.

## Server Scoping

Each game server has a `ServerId` in its config (e.g. `"eu-1"`, `"na-2"`, `"scrim-server"`). The default is `"0"` which means global.

When the game server queries the database, it loads entries where:
```sql
WHERE ServerId = @thisServer OR ServerId = '0'
```

This means:
- `ServerId = "0"` entries are visible to ALL servers (global whitelist)
- `ServerId = "eu-1"` entries are visible ONLY to the server configured with `ServerId = "eu-1"`
- If a player has both a global entry and a server-specific entry, the server-specific entry takes priority

### Web UI implications

Your server selector/filter in the UI should show:
- **Global** (ServerId = "0") — applies to all servers
- Each configured server by its ID

When adding a player, the admin picks the target server (or global). When listing, you can filter by server or show all.

## Web UI Flow

### Landing page: Whitelist Dashboard

Show an overview:
- Total whitelisted players (per server + global)
- Breakdown by group (e.g. "streamers: 5, content: 12, vip: 34, whitelist: 128")
- Quick search bar

### Main view: Player List

A table with columns:
- Player name (from `PlayerName` column, with a Steam profile link using `SteamId`)
- SteamID64
- Group (with a colored badge: streamer=red, content=yellow, vip=purple, whitelist=green or similar)
- Server (show "Global" for ServerId "0", otherwise the server name)
- Added at (human-readable date)
- Added by (show the name if you can resolve the SteamID, otherwise raw value)
- Actions (Edit group, Remove)

Features:
- **Filter by server** — dropdown: All, Global, server1, server2, ...
- **Filter by group** — dropdown or toggle chips
- **Search** — by player name or SteamID64
- **Sort** — by any column
- **Pagination** — if you have thousands of entries

### Add Player

Form fields:
- **SteamID64** (required) — validate it's a 17-digit number starting with `7656`
- **Player Name** (optional but recommended) — free text, helps admins identify players later
- **Group** (required) — dropdown with available groups: `streamer`, `content`, `vip`, `whitelist`
- **Server** (required) — dropdown: Global, or specific server IDs
- **Added By** — auto-fill from your web auth system (logged-in admin username, or their SteamID if using Steam auth)

On submit:
```sql
-- Remove existing entry for this player on this server (if any)
DELETE FROM whitelist WHERE SteamId = @steamId AND ServerId = @serverId;

-- Insert new entry
INSERT INTO whitelist (ServerId, SteamId, PlayerName, GroupName, AddedAt, AddedBy)
VALUES (@serverId, @steamId, @playerName, @groupName, UTC_TIMESTAMP(), @addedBy);
```

The game server will pick up the change within 30 seconds.

### Edit Player

Same form as Add, but pre-filled. Only `GroupName` and `ServerId` should be editable (changing a SteamID is semantically a delete + add). Update by `Id`.

### Remove Player

Confirm dialog, then:
```sql
DELETE FROM whitelist WHERE Id = @id;
```

### Bulk Operations

Nice to have:
- **Bulk add** — paste a list of SteamID64s (one per line), assign them all to a group + server
- **Bulk remove** — select multiple rows, delete all
- **Import/Export CSV** — columns: SteamId, PlayerName, GroupName, ServerId

## Steam Integration (Optional)

If your web app can call the Steam Web API:
- Resolve SteamID64 to profile name + avatar via `ISteamUser/GetPlayerSummaries`
- Show the avatar next to the player name in the list
- Auto-fill PlayerName when adding by SteamID64
- Link to Steam profile: `https://steamcommunity.com/profiles/{SteamId}`

This is optional — the system works fine with just SteamID64 and manually entered names.

## Sync Behavior

The game server does NOT use the database in real-time. Here's how sync works:

1. On startup: game server loads all entries from DB into an in-memory `ConcurrentDictionary<SteamId, GroupName>`
2. Every 30 seconds: game server runs `SELECT COUNT(*) FROM whitelist WHERE ServerId = @id OR ServerId = '0'`
3. If the count differs from the in-memory cache, it re-fetches all rows and rebuilds the cache
4. Player connection checks (allow/deny) run against the in-memory cache, NOT the database

This means:
- Changes from the web UI take up to 30 seconds to be reflected in-game
- You do NOT need to notify the game server of changes
- There is no API endpoint to call — just write to the database
- If you want instant effect, an admin can run `wl refresh` in-game to force an immediate re-sync

## Example Queries

### List all players on a specific server (including global)
```sql
SELECT * FROM whitelist
WHERE ServerId = 'eu-1' OR ServerId = '0'
ORDER BY GroupName, PlayerName;
```

### Count players per group per server
```sql
SELECT ServerId, GroupName, COUNT(*) as Count
FROM whitelist
GROUP BY ServerId, GroupName
ORDER BY ServerId, GroupName;
```

### Search by name or SteamID
```sql
SELECT * FROM whitelist
WHERE PlayerName LIKE '%search%' OR CAST(SteamId AS CHAR) LIKE '%search%'
ORDER BY AddedAt DESC;
```

### Get all unique server IDs (for populating a server dropdown)
```sql
SELECT DISTINCT ServerId FROM whitelist ORDER BY ServerId;
```

### Check if a specific player is whitelisted on a server
```sql
SELECT * FROM whitelist
WHERE SteamId = 76561198012345678
  AND (ServerId = 'eu-1' OR ServerId = '0');
```

### Audit: who added entries recently
```sql
SELECT * FROM whitelist
ORDER BY AddedAt DESC
LIMIT 50;
```

## Data Validation Rules

- `SteamId` must be a valid Steam ID 64 (unsigned 64-bit integer, typically 17 digits starting with `7656`)
- `GroupName` must be one of the configured groups (`streamer`, `content`, `vip`, `whitelist`). If you allow custom groups, they must also be added to the game server's `whitelist.jsonc` config or they will be ignored
- `ServerId` should be `"0"` for global, or match a game server's configured `ServerId`
- `PlayerName` is display-only and not used for any logic. It can be stale — the game server updates it when the player connects and is added via in-game command
- `AddedBy` is an audit field. Use your auth system's user identifier (SteamID64 of the web admin, username, email, etc.)
- One entry per `(ServerId, SteamId)` pair — enforce this in your application logic

## Architecture Diagram

```
┌─────────────────┐     ┌──────────────────┐     ┌─────────────────┐
│   Web UI        │     │   MySQL DB       │     │  Game Server    │
│                 │     │                  │     │                 │
│  Add/Edit/      │────>│  whitelist table  │<────│  Polls every    │
│  Remove players │     │                  │     │  30 seconds     │
│                 │     │                  │     │                 │
│  Read player    │<────│                  │     │  In-memory      │
│  list           │     │                  │     │  cache for      │
│                 │     │                  │     │  player checks  │
└─────────────────┘     └──────────────────┘     └─────────────────┘
                                                        │
                                                        │ OnClientPreAdminCheck
                                                        │ (synchronous, from cache)
                                                        ▼
                                                  Allow / Kick player
```

No direct communication between web UI and game server. The database is the single source of truth. The game server's in-memory cache is a performance optimization — it always converges to the database state within 30 seconds.
