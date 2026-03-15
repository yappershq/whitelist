using SqlSugar;

namespace WhiteList.Request.Sql;

[SugarTable("whitelist")]
public class WhiteListEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public int Id { get; set; }

    [SugarColumn(Length = 32, DefaultValue = "0")]
    public string ServerId { get; set; } = "0";

    public ulong SteamId { get; set; }

    [SugarColumn(Length = 64, IsNullable = true)]
    public string? PlayerName { get; set; }

    [SugarColumn(Length = 32)]
    public string GroupName { get; set; } = "whitelist";

    public DateTime AddedAt { get; set; }

    [SugarColumn(Length = 64, IsNullable = true)]
    public string? AddedBy { get; set; }
}
