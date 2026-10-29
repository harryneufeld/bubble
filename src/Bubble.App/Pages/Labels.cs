using Bubble.Core;

namespace Bubble.App.Pages;

/// <summary>Anzeige-Texte, die mehrere Seiten teilen.</summary>
static class Labels
{
    public static string Lifetime(PostLifetime lifetime) => lifetime switch
    {
        PostLifetime.Story24h => "⏳ 24 Stunden",
        _ => "📌 Dauerhaft",
    };

    /// <summary>Auswahlzeilen für die Lebensdauer; die aktuelle Einstellung ist markiert.</summary>
    public static IReadOnlyList<Controls.SheetOption> LifetimeOptions(PostLifetime? current = null) =>
    [
        new("24 Stunden", "Highlights verschwinden nach einem Tag, wie Stories.", "⏳", current == PostLifetime.Story24h),
        new("Dauerhaft", "Highlights bleiben, bis sie jemand löscht.", "📌", current == PostLifetime.Persistent),
    ];

    public static PostLifetime LifetimeAt(int index) => index == 0 ? PostLifetime.Story24h : PostLifetime.Persistent;

    public static string Remaining(BubbleMessage message)
    {
        if (message.ExpiresAt is not { } expires) return "📌 bleibt";
        var left = expires - DateTimeOffset.UtcNow;
        return left.TotalHours >= 1 ? $"⏳ noch {(int)left.TotalHours} h" : $"⏳ noch {Math.Max(1, (int)left.TotalMinutes)} min";
    }

    public static string Mood(Mood? mood) => mood switch
    {
        Core.Mood.Top => "👍 Top",
        Core.Mood.Flop => "👎 Flop",
        _ => "",
    };

    /// <summary>Unterscheidet „Server nicht erreichbar“ von „Server hat abgelehnt“.</summary>
    public static string ConnectionError(string relayUrl, HttpRequestException ex) => ex.StatusCode is { } status
        ? $"Der Server {Host(relayUrl)} hat die Anfrage abgelehnt (HTTP {(int)status})."
        : $"Der Server {Host(relayUrl)} ist nicht erreichbar.";

    public static string Host(string relayUrl) => Uri.TryCreate(relayUrl, UriKind.Absolute, out var uri) ? uri.Authority : relayUrl;

    /// <summary>„gerade eben“, „vor 5 min“, „vor 3 h“, „gestern, 18:20“, „12.10., 09:15“.</summary>
    public static string Ago(DateTimeOffset createdAt)
    {
        var age = DateTimeOffset.UtcNow - createdAt;
        if (age.TotalMinutes < 1) return "gerade eben";
        if (age.TotalHours < 1) return $"vor {(int)age.TotalMinutes} min";
        var local = createdAt.ToLocalTime();
        if (local.Date == DateTime.Today) return $"vor {(int)age.TotalHours} h";
        if (local.Date == DateTime.Today.AddDays(-1)) return $"gestern, {local:HH:mm}";
        return local.ToString("dd.MM., HH:mm");
    }

    public static string People(int count) => count == 1 ? "1 Person" : $"{count} Leute";
}
