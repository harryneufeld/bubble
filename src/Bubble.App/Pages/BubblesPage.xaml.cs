using Bubble.App.Services;
using Bubble.Core;

namespace Bubble.App.Pages;

public sealed record BubbleItem(string MailboxId, string Name, PostLifetime Kind, string Server)
{
    public string Lifetime => Labels.Lifetime(Kind);
    public bool IsStory => Kind == PostLifetime.Story24h;
    public bool IsPersistent => !IsStory;
}

public partial class BubblesPage : ContentPage
{
    readonly AccountService _account;

    public BubblesPage(AccountService account)
    {
        InitializeComponent();
        _account = account;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!await _account.LoadAsync())
        {
            await Shell.Current.GoToAsync(nameof(OnboardingPage));
            return;
        }
        Refresh();
    }

    void Refresh() => List.ItemsSource = _account.Vault!.Bubbles
        .Select(b => new BubbleItem(b.MailboxId, b.Name, b.Lifetime, new Uri(b.RelayUrl).Authority))
        .ToList();

    async void OnTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not BubbleItem item) return;
        await Shell.Current.GoToAsync($"{nameof(FeedPage)}?id={Uri.EscapeDataString(item.MailboxId)}");
    }

    async void OnCreate(object? sender, EventArgs e)
    {
        var name = await DisplayPromptAsync("Neue Bubble", "Wie soll die Bubble heißen?", "Weiter", "Abbrechen", "z. B. Familie, WG, Laufgruppe");
        if (string.IsNullOrWhiteSpace(name)) return;

        var story = Labels.Lifetime(PostLifetime.Story24h);
        var persistent = Labels.Lifetime(PostLifetime.Persistent);
        var choice = await DisplayActionSheetAsync("Wie lange sollen Posts sichtbar sein?", "Abbrechen", null, story, persistent);
        if (choice != story && choice != persistent) return;

        var lifetime = choice == story ? PostLifetime.Story24h : PostLifetime.Persistent;
        _account.Vault!.Bubbles.Add(BubbleMembership.Create(name.Trim(), AccountService.DefaultRelayUrl, lifetime));
        await SaveAndRefresh();
    }

    async void OnJoin(object? sender, EventArgs e)
    {
        var clip = Clipboard.Default.HasText ? await Clipboard.Default.GetTextAsync() : null;
        var prefill = clip is not null && Invite.TryParse(clip, out _) ? clip : "";
        var link = await DisplayPromptAsync("Bubble beitreten", "Füge den Einladungslink ein:", "Beitreten", "Abbrechen", "bubble://join#…", initialValue: prefill);
        if (string.IsNullOrWhiteSpace(link)) return;

        if (!Invite.TryParse(link, out var invite))
        {
            await DisplayAlertAsync("Ungültiger Link", "Das sieht nicht nach einer Bubble-Einladung aus.", "OK");
            return;
        }
        var bubble = invite!.ToMembership();
        if (_account.FindBubble(bubble.MailboxId) is not null)
        {
            await DisplayAlertAsync("Schon dabei", $"Du bist bereits in „{bubble.Name}“.", "OK");
            return;
        }
        _account.Vault!.Bubbles.Add(bubble);
        await SaveAndRefresh();
    }

    async void OnBackup(object? sender, EventArgs e) => await Shell.Current.GoToAsync(nameof(BackupPage));

    async void OnServer(object? sender, EventArgs e)
    {
        var input = await DisplayPromptAsync("Server",
            "Gilt für neue Bubbles und dein Backup. Bestehende Bubbles bleiben auf ihrem Server.",
            "Speichern", "Abbrechen", "192.168.1.20:5080", initialValue: AccountService.DefaultRelayUrl, keyboard: Keyboard.Url);
        if (input is null) return;
        if (!AccountService.TryNormalizeRelayUrl(input, out var url))
        {
            await DisplayAlertAsync("Ungültige Server-Adresse", "Beispiel: 192.168.1.20:5080", "OK");
            return;
        }
        if (!await AccountService.IsReachableAsync(url))
        {
            await DisplayAlertAsync("Server nicht erreichbar", $"Unter {url} antwortet kein Bubble-Relay.", "OK");
            return;
        }
        AccountService.DefaultRelayUrl = url;
        await SaveAndRefresh(); // lädt ein eingerichtetes Backup auf den neuen Server
    }

    async Task SaveAndRefresh()
    {
        var backupOk = await _account.SaveAsync();
        Refresh();
        if (!backupOk)
            await DisplayAlertAsync("Backup nicht aktualisiert", "Der Server ist nicht erreichbar. Das Backup wird beim nächsten Speichern aktualisiert.", "OK");
    }
}
