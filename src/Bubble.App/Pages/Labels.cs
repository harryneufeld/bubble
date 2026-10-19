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

    public static string Remaining(BubbleMessage message)
    {
        if (message.ExpiresAt is not { } expires) return "📌 dauerhaft";
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
        ? $"Der Server {relayUrl} hat die Anfrage abgelehnt (HTTP {(int)status})."
        : $"Der Server {relayUrl} ist nicht erreichbar.";

    public static string Time(DateTimeOffset createdAt)
    {
        var local = createdAt.ToLocalTime();
        return local.Date == DateTime.Today ? $"heute, {local:HH:mm}" : local.ToString("dd.MM., HH:mm");
    }
}
