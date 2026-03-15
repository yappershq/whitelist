using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using WhiteList.Shared;

namespace WhiteList.VipChecker.MySql;

public class MysqlVipChecker : IWhiteListGroupProvider, IDisposable
{
    private readonly string                              _connectionString;
    private readonly string                              _serverId;
    private readonly string                              _groupName;
    private readonly string                              _cacheFilePath;
    private readonly ILogger<MysqlVipChecker>            _logger;
    private readonly ConcurrentDictionary<ulong, byte>   _cache = new();

    private Timer? _syncTimer;

    public MysqlVipChecker(string         connectionString,
                           string         serverId,
                           string         groupName,
                           string         dataPath,
                           ILoggerFactory loggerFactory)
    {
        _connectionString = connectionString;
        _serverId         = serverId;
        _groupName        = groupName;
        _cacheFilePath    = Path.Combine(dataPath, "vip_cache.json");
        _logger           = loggerFactory.CreateLogger<MysqlVipChecker>();
    }

    public void Init()
    {
        LoadCacheFromFile();
        SyncFromDatabase();

        _syncTimer = new Timer(_ => SyncFromDatabase(), null,
            TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));

        _logger.LogInformation("MysqlVipChecker initialized with {Count} cached VIPs", _cache.Count);
    }

    public string? GetPlayerGroup(ulong steamId)
    {
        return _cache.ContainsKey(steamId) ? _groupName : null;
    }

    private void SyncFromDatabase()
    {
        try
        {
            using var conn = new MySqlConnection(_connectionString);
            conn.Open();

            // Check count
            using var countCmd = conn.CreateCommand();
            countCmd.CommandText = """
                SELECT COUNT(*) FROM ms_vips
                WHERE (expires = 0 OR expires > UNIX_TIMESTAMP())
                  AND (server_id = '0' OR server_id = @serverId)
                """;
            countCmd.Parameters.AddWithValue("@serverId", _serverId);

            var count = Convert.ToInt32(countCmd.ExecuteScalar());

            if (count == _cache.Count) return;

            // Fetch all active VIPs
            using var fetchCmd = conn.CreateCommand();
            fetchCmd.CommandText = """
                SELECT steamid64 FROM ms_vips
                WHERE (expires = 0 OR expires > UNIX_TIMESTAMP())
                  AND (server_id = '0' OR server_id = @serverId)
                """;
            fetchCmd.Parameters.AddWithValue("@serverId", _serverId);

            _cache.Clear();

            using var reader = fetchCmd.ExecuteReader();
            while (reader.Read())
            {
                var steamId = (ulong)reader.GetInt64(0);
                _cache[steamId] = 0;
            }

            SaveCacheToFile();
            _logger.LogInformation("VIP cache synced from database: {Count} entries", _cache.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync VIP cache from database");
        }
    }

    private void LoadCacheFromFile()
    {
        if (!File.Exists(_cacheFilePath)) return;

        try
        {
            var json     = File.ReadAllText(_cacheFilePath);
            var steamIds = JsonSerializer.Deserialize<List<string>>(json);

            if (steamIds is null) return;

            foreach (var id in steamIds)
            {
                if (ulong.TryParse(id, out var steamId))
                {
                    _cache[steamId] = 0;
                }
            }

            _logger.LogInformation("Loaded {Count} entries from VIP cache file", _cache.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load VIP cache from {Path}", _cacheFilePath);
        }
    }

    private void SaveCacheToFile()
    {
        try
        {
            var dir = Path.GetDirectoryName(_cacheFilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var steamIds = _cache.Keys.Select(k => k.ToString()).ToList();
            var json     = JsonSerializer.Serialize(steamIds, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_cacheFilePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save VIP cache to {Path}", _cacheFilePath);
        }
    }

    public void Dispose()
    {
        _syncTimer?.Dispose();
    }
}
