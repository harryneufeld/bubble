using System.ComponentModel;
using Bubble.App.Services;
using Bubble.Core;

namespace Bubble.App.Pages;

public sealed record CommentItem(string Name, byte[]? Avatar, string Text, string When, bool IsMine);

/// <summary>Anzeigedaten einer Post-Karte (Home, Bubble-Seite und Post-Seite). Herzen und Kommentare ändern sich live.</summary>
public sealed class PostItem(HomePost item, FeedService feed, bool showBubble, bool showFaces = false) : INotifyPropertyChanged
{
    readonly BubbleMessage _m = item.Post.Message;

    public event PropertyChangedEventHandler? PropertyChanged;

    public HomePost Source => item;
    public string Id => item.Post.Id;
    public string MailboxId => item.Bubble.MailboxId;
    public string Author { get; } = item.Post.IsMine ? "Du" : item.Author?.Name ?? item.Post.Message.AuthorName;
    public byte[]? AuthorAvatar { get; } = item.Author?.Avatar;
    public string AuthorName => item.Author?.Name ?? _m.AuthorName;
    public string Meta { get; } = showBubble
        ? $"{item.Bubble.Name} · {Labels.Ago(item.Post.Message.CreatedAt)}"
        : Labels.Ago(item.Post.Message.CreatedAt);
    public string Feeling { get; } = Labels.Feeling(item.Post.Message);
    public bool HasFeeling => Feeling.Length > 0;
    public string Remaining { get; } = Labels.Remaining(item.Post.Message);
    public string? Text => _m.Text;
    public bool HasImage => _m.Image is not null;
    public bool IsTextOnly => _m.Image is null;
    public bool HasCaption => HasImage && Text is not null;
    public ImageSource? Image { get; } = item.Post.Message.Image is { } bytes
        ? Sources.GetValue(bytes, b => ImageSource.FromStream(() => new MemoryStream(b)))
        : null;
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<byte[], ImageSource> Sources = new();
    public string ImageDescription => $"Foto von {AuthorName}";
    public Brush TextBackground { get; } = Gradients.For(item.Post.Id);

    // ── Herzen und Kommentare ──────────────────────────────

    public bool IsLiked => item.Snapshot.IsLikedByMe(Id);
    public string LikeIcon => IsLiked ? "ic_heart_bubble_on.png" : "ic_heart_bubble.png";
    public string LikeDescription => IsLiked ? "Herz zurücknehmen" : "Herz geben";

    /// <summary>Gesichter der Herz-Geber. Nur auf der Post-Seite: im Feed kostet jeder Avatar pro Karte Scroll-Leistung, dort steht nur der Text.</summary>
    public IReadOnlyList<Face> Likers => showFaces ? item.Snapshot.LikersOf(Id).Take(3).Select(m => new Face(m.Name, m.Avatar)).ToList() : [];
    public bool HasLikers => item.Snapshot.LikersOf(Id).Count > 0;

    /// <summary>„Mia“, „Mia und Tom“, „Mia, Tom und 2 weitere“. Eigenes Herz heißt „Du“.</summary>
    public string LikersText
    {
        get
        {
            var names = item.Snapshot.LikersOf(Id).Select(m => m.IsMe ? "Du" : First(m.Name)).ToList();
            return names.Count switch
            {
                0 => "",
                1 => names[0],
                2 => $"{names[0]} und {names[1]}",
                3 => $"{names[0]}, {names[1]} und {names[2]}",
                var n => $"{names[0]}, {names[1]} und {n - 2} weitere",
            };
        }
    }

    public IReadOnlyList<CommentItem> Comments => item.Snapshot.CommentsOf(Id)
        .Select(c =>
        {
            var member = item.Snapshot.MemberFor(c.AuthorKey);
            var mine = member?.IsMe == true;
            return new CommentItem(mine ? "Du" : member?.Name ?? c.AuthorName, member?.Avatar, c.Text, Labels.Ago(c.CreatedAt), mine);
        })
        .ToList();

    public string CommentsText => item.Snapshot.CommentsOf(Id).Count switch
    {
        0 => "Kommentieren",
        1 => "1 Kommentar",
        var n => $"{n} Kommentare",
    };

    /// <summary>Herz setzen oder zurücknehmen. Meldet Fehler selbst und stellt den alten Zustand wieder her.</summary>
    public async Task ToggleLikeAsync()
    {
        try
        {
            var task = feed.ToggleLikeAsync(item);
            Changed(); // sofort anzeigen
            await task;
        }
        catch (HttpRequestException ex)
        {
            Changed();
            if (Shell.Current is { } shell)
                await shell.DisplayAlertAsync("Herz nicht gesendet", Labels.ConnectionError(item.Bubble.RelayUrl, ex), "OK");
        }
        catch (InvalidOperationException ex)
        {
            Changed();
            if (Shell.Current is { } shell) await shell.DisplayAlertAsync("Nicht möglich", ex.Message, "OK");
        }
    }

    public void Changed()
    {
        foreach (var name in new[] { nameof(IsLiked), nameof(LikeIcon), nameof(LikeDescription), nameof(Likers), nameof(HasLikers), nameof(LikersText), nameof(Comments), nameof(CommentsText) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    static string First(string name) => name.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? name;
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
