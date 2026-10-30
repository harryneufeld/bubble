using Bubble.Core;

namespace Bubble.App.Pages;

/// <summary>Display texts shared by several pages.</summary>
static class Labels
{
    public static string Lifetime(PostLifetime lifetime) => lifetime switch
    {
        PostLifetime.Story24h => "⏳ 24 hours",
        _ => "📌 Permanent",
    };

    /// <summary>Option rows for the lifetime; the current setting is marked.</summary>
    public static IReadOnlyList<Controls.SheetOption> LifetimeOptions(PostLifetime? current = null) =>
    [
        new("24 hours", "Highlights disappear after a day, like stories.", "⏳", current == PostLifetime.Story24h),
        new("Permanent", "Highlights stay until someone deletes them.", "📌", current == PostLifetime.Persistent),
    ];

    public static PostLifetime LifetimeAt(int index) => index == 0 ? PostLifetime.Story24h : PostLifetime.Persistent;

    public static string Remaining(BubbleMessage message)
    {
        if (message.ExpiresAt is not { } expires) return "📌 stays";
        var left = expires - DateTimeOffset.UtcNow;
        return left.TotalHours >= 1 ? $"⏳ {(int)left.TotalHours} h left" : $"⏳ {Math.Max(1, (int)left.TotalMinutes)} min left";
    }

    /// <summary>Emojis to choose from for "How are you feeling?".</summary>
    public static readonly (string Emoji, string Label)[] Feelings =
    [
        ("😄", "Happy"), ("😍", "In love"), ("🥳", "Celebrating"), ("😎", "Cool"),
        ("😌", "Relaxed"), ("🤩", "Excited"), ("🥹", "Touched"), ("😋", "Yummy"),
        ("💪", "Strong"), ("😊", "Content"), ("😴", "Tired"), ("😢", "Sad"),
        ("😤", "Annoyed"), ("😩", "Exhausted"), ("🤯", "Overwhelmed"), ("😕", "Meh"),
    ];

    /// <summary>A post's feeling as "😍 In love". Old top/flop posts are translated. Empty if none is set.</summary>
    public static string Feeling(BubbleMessage message)
    {
        var emoji = message.Emoji ?? message.Mood switch { Core.Mood.Top => "😊", Core.Mood.Flop => "😕", _ => null };
        if (emoji is null) return "";
        var label = message.Emoji is null ? (message.Mood == Core.Mood.Top ? "Top" : "Flop") : Feelings.FirstOrDefault(f => f.Emoji == emoji).Label;
        return string.IsNullOrEmpty(label) ? emoji : $"{emoji} {label}";
    }

    /// <summary>Tells "server not reachable" apart from "server refused".</summary>
    public static string ConnectionError(string relayUrl, HttpRequestException ex) => ex.StatusCode is { } status
        ? $"The server {Host(relayUrl)} refused the request (HTTP {(int)status})."
        : $"The server {Host(relayUrl)} is not reachable.";

    public static string Host(string relayUrl) => Uri.TryCreate(relayUrl, UriKind.Absolute, out var uri) ? uri.Authority : relayUrl;

    /// <summary>"just now", "5 min ago", "3 h ago", "yesterday, 18:20", "12 Oct, 09:15".</summary>
    public static string Ago(DateTimeOffset createdAt)
    {
        var age = DateTimeOffset.UtcNow - createdAt;
        if (age.TotalMinutes < 1) return "just now";
        if (age.TotalHours < 1) return $"{(int)age.TotalMinutes} min ago";
        var local = createdAt.ToLocalTime();
        if (local.Date == DateTime.Today) return $"{(int)age.TotalHours} h ago";
        if (local.Date == DateTime.Today.AddDays(-1)) return $"yesterday, {local:HH:mm}";
        return local.ToString("d MMM, HH:mm", System.Globalization.CultureInfo.InvariantCulture);
    }

    public static string People(int count) => count == 1 ? "1 person" : $"{count} people";
}
