using Bubble.App.Services;
using Bubble.Core;

namespace Bubble.App.Pages;

/// <summary>Anzeigedaten einer Post-Karte (Home und Bubble-Seite).</summary>
public sealed class PostItem(HomePost item, bool showBubble)
{
    readonly BubbleMessage _m = item.Post.Message;

    public string Id => item.Post.Id;
    public string MailboxId => item.Bubble.MailboxId;
    public string Author { get; } = item.Post.IsMine ? "Du" : item.Author?.Name ?? item.Post.Message.AuthorName;
    public byte[]? AuthorAvatar { get; } = item.Author?.Avatar;
    public string AuthorName => item.Author?.Name ?? _m.AuthorName;
    public string Meta { get; } = showBubble
        ? $"{item.Bubble.Name} · {Labels.Ago(item.Post.Message.CreatedAt)}"
        : Labels.Ago(item.Post.Message.CreatedAt);
    public string Mood => Labels.Mood(_m.Mood);
    public bool IsTop => _m.Mood == Core.Mood.Top;
    public bool IsFlop => _m.Mood == Core.Mood.Flop;
    public string Remaining { get; } = Labels.Remaining(item.Post.Message);
    public string? Text => _m.Text;
    public bool HasImage => _m.Image is not null;
    public bool IsTextOnly => _m.Image is null;
    public bool HasCaption => HasImage && Text is not null;
    public ImageSource? Image { get; } = item.Post.Message.Image is { } bytes ? ImageSource.FromStream(() => new MemoryStream(bytes)) : null;
    public string ImageDescription => $"Foto von {AuthorName}";
    public Brush TextBackground { get; } = Gradients.For(item.Post.Id);
}

/// <summary>Leuchtende Verläufe für Text-Posts und Bubble-Cover, stabil je ID.</summary>
static class Gradients
{
    static readonly (string, string)[] Pairs =
    [
        ("#D9246F", "#6D3CF5"), ("#6D3CF5", "#0E7490"), ("#C2410C", "#BE185D"),
        ("#0F766E", "#4338CA"), ("#7E22CE", "#DB2777"), ("#1D4ED8", "#7C3AED"),
    ];

    public static Brush For(string id)
    {
        var h = 0;
        foreach (var c in id) h = unchecked(h * 31 + c);
        var (from, to) = Pairs[(uint)h % Pairs.Length];
        return new LinearGradientBrush(
            [new GradientStop(Color.FromArgb(from), 0), new GradientStop(Color.FromArgb(to), 1)],
            new Point(0, 0), new Point(1, 1));
    }
}
