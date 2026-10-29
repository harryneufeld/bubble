using Bubble.App.Controls;
using Bubble.App.Services;

namespace Bubble.App.Pages;

public partial class ProfilePage : ContentPage
{
    readonly AccountService _account;

    public ProfilePage(AccountService account)
    {
        InitializeComponent();
        _account = account;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!await _account.LoadAsync()) return;
        var vault = _account.Vault!;
        Avatar.Name = vault.DisplayName;
        Avatar.Image = vault.Avatar;
        NameLabel.Text = vault.DisplayName;
        BioLabel.Text = vault.Bio ?? "";
        BioLabel.IsVisible = vault.Bio is not null;
        StatsLabel.Text = vault.Bubbles.Count == 1 ? "In 1 Bubble" : $"In {vault.Bubbles.Count} Bubbles";
        BackupLabel.Text = vault.Backup is null ? "Noch nicht eingerichtet" : "Aktiv, wird automatisch aktualisiert";
        ServerLabel.Text = Labels.Host(AccountService.DefaultRelayUrl);
        VersionLabel.Text = $"Version {AppInfo.Current.VersionString} ({AppInfo.Current.BuildString})";
    }

    async void OnGitHub(object? sender, TappedEventArgs e) => await OpenAsync("https://github.com/harryneufeld/bubble");

    async void OnWebsite(object? sender, TappedEventArgs e) => await OpenAsync("https://harry-neufeld.de");

    async Task OpenAsync(string url)
    {
        try
        {
            if (!await Launcher.Default.OpenAsync(new Uri(url)))
                await DisplayAlertAsync("Link nicht geöffnet", $"Öffne {url} bitte selbst im Browser.", "OK");
        }
        catch (Exception)
        {
            // kein Browser gefunden oder nicht erlaubt
            await DisplayAlertAsync("Link nicht geöffnet", $"Öffne {url} bitte selbst im Browser.", "OK");
        }
    }

    async void OnEdit(object? sender, EventArgs e) => await Shell.Current.GoToAsync(nameof(ProfileEditPage));

    async void OnBackup(object? sender, TappedEventArgs e) => await Shell.Current.GoToAsync(nameof(BackupPage));

    async void OnServer(object? sender, TappedEventArgs e)
    {
        var input = await SheetPage.PromptAsync(this, "Server",
            "Gilt für neue Bubbles und dein Backup. Bestehende Bubbles bleiben auf ihrem Server.",
            "Speichern", "192.168.1.20:5080", Labels.Host(AccountService.DefaultRelayUrl), 100, Keyboard.Url);
        if (input is null) return;
        if (!AccountService.TryNormalizeRelayUrl(input, out var url))
        {
            await DisplayAlertAsync("Ungültige Server-Adresse", "Beispiel: 192.168.1.20:5080", "OK");
            return;
        }
        if (!await AccountService.IsReachableAsync(url))
        {
            await DisplayAlertAsync("Server nicht erreichbar", $"Unter {Labels.Host(url)} antwortet kein Bubble-Relay.", "OK");
            return;
        }
        AccountService.DefaultRelayUrl = url;
        var backupOk = await _account.SaveAsync(); // lädt ein eingerichtetes Backup auf den neuen Server
        ServerLabel.Text = Labels.Host(url);
        if (!backupOk)
            await DisplayAlertAsync("Backup nicht aktualisiert", "Der Server ist nicht erreichbar. Das Backup wird beim nächsten Speichern aktualisiert.", "OK");
    }
}
