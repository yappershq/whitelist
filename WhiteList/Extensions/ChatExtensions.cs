using System.Globalization;
using Sharp.Shared.Definition;
using Sharp.Shared.Enums;
using Sharp.Shared.Objects;

namespace WhiteList.Extensions;

internal static class ChatExtensions
{
    private static readonly (string Placeholder, string Code)[] ColorMap =
    [
        ("{white}",      ChatColor.White),   ("{default}",    ChatColor.White),
        ("{darkred}",    ChatColor.DarkRed), ("{pink}",       ChatColor.Pink),
        ("{green}",      ChatColor.Green),   ("{lightgreen}", ChatColor.LightGreen),
        ("{lime}",       ChatColor.Lime),    ("{red}",        ChatColor.Red),
        ("{grey}",       ChatColor.Grey),    ("{gray}",       ChatColor.Grey),
        ("{yellow}",     ChatColor.Yellow),  ("{gold}",       ChatColor.Gold),
        ("{silver}",     ChatColor.Silver),  ("{blue}",       ChatColor.Blue),
        ("{lightblue}",  ChatColor.Blue),    ("{darkblue}",   ChatColor.DarkBlue),
        ("{purple}",     ChatColor.Purple),  ("{lightred}",   ChatColor.LightRed),
        ("{orange}",     ChatColor.Yellow),
    ];

    private const string Prefix = " \x02[WhiteList]\x01 ";

    public static string ApplyColors(this string message)
    {
        if (string.IsNullOrEmpty(message) || !message.Contains('{')) return message;
        foreach (var (placeholder, code) in ColorMap)
            message = message.Replace(placeholder, code, StringComparison.OrdinalIgnoreCase);
        return message;
    }

    public static string Loc(string key, params object?[] args)
    {
        var bridge = InterfaceBridge.Instance;
        if (bridge is null) return key;

        try
        {
            var instance = bridge.GetLocalizerManager();
            if (args.Length == 0) return instance.Format(CultureInfo.InvariantCulture, key);

            var anyPlayer = bridge.ClientManager.GetGameClientList(true)
                .FirstOrDefault(p => p is { IsValid: true, IsInGame: true, IsFakeClient: false });

            return anyPlayer is null
                ? instance.Format(CultureInfo.InvariantCulture, key, args)
                : instance.For(anyPlayer).Text(key, args);
        }
        catch
        {
            return key;
        }
    }

    public static void ReplyLocale(IGameClient? caller, string key, params object?[] args)
    {
        var bridge = InterfaceBridge.Instance;
        if (bridge is null) return;

        if (caller is null)
        {
            Console.WriteLine($" [WhiteList] {Loc(key, args)}");
            return;
        }

        try
        {
            var instance = bridge.GetLocalizerManager();
            instance.For(caller)
                .Message()
                .Prefix(Prefix)
                .Transform(s => s.ApplyColors())
                .Text(key, args)
                .Print();
        }
        catch
        {
            caller.GetPlayerController()?.Print(HudPrintChannel.SayText2, $"{Prefix}{Loc(key, args)}");
        }
    }

    public static void ReplyLiteral(IGameClient? caller, string message)
    {
        if (caller is null)
        {
            Console.WriteLine($" [WhiteList] {message}");
            return;
        }

        caller.GetPlayerController()?.Print(HudPrintChannel.SayText2, $"{Prefix}{message}");
    }

    public static void PrintLocaleAll(string key, params object?[] args)
    {
        var bridge = InterfaceBridge.Instance;
        if (bridge is null) return;

        try
        {
            var instance = bridge.GetLocalizerManager();
            var players = bridge.ClientManager.GetGameClientList(true)
                .Where(p => p is { IsValid: true, IsInGame: true, IsFakeClient: false })
                .ToList();

            if (players.Count == 0) return;

            instance.ForMany(players)
                .Message()
                .Prefix(Prefix)
                .Transform(s => s.ApplyColors())
                .Text(key, args)
                .Print();
        }
        catch
        {
            // Localizer not available yet
        }
    }
}
