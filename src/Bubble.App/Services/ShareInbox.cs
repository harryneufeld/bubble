namespace Bubble.App.Services;

/// <summary>Content another app shared with Bubble ("Share" → Bubble), waiting to become a highlight.</summary>
public static class ShareInbox
{
    public sealed record Shared(byte[]? Image, string? Text);

    static Shared? _pending;
    static bool _ready;

    /// <summary>Set by the home page once an account is loaded; before that there is nowhere to post from.</summary>
    public static void MarkReady()
    {
        _ready = true;
        Open();
    }

    public static void Offer(Shared shared)
    {
        _pending = shared;
        MainThread.BeginInvokeOnMainThread(Open);
    }

    public static Shared? Take()
    {
        var shared = _pending;
        _pending = null;
        return shared;
    }

    static async void Open()
    {
        if (_pending is null || !_ready || Shell.Current is not AppShell shell) return;
        // A compose page that is already open would not pick the content up: start from the home tab.
        if (shell.Navigation.NavigationStack.Count > 1 || shell.Navigation.ModalStack.Count > 0) await shell.GoToAsync("//home");
        await shell.GoToAsync(nameof(Pages.ComposePage));
    }
}
