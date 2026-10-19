using Bubble.App.Services;

namespace Bubble.App.Pages;

public partial class OnboardingPage : ContentPage
{
    readonly AccountService _account;

    public OnboardingPage(AccountService account)
    {
        InitializeComponent();
        _account = account;
        ServerEntry.Text = AccountService.DefaultRelayUrl;
    }

    async Task<bool> ApplyServerAsync()
    {
        if (!AccountService.TryNormalizeRelayUrl(ServerEntry.Text, out var url))
        {
            await DisplayAlertAsync("Ungültige Server-Adresse", "Beispiel: 192.168.1.20:5080", "OK");
            return false;
        }
        if (!await AccountService.IsReachableAsync(url))
        {
            await DisplayAlertAsync("Server nicht erreichbar", $"Unter {url} antwortet kein Bubble-Relay. Bitte prüfe die Adresse unten im Feld „Server“.", "OK");
            return false;
        }
        AccountService.DefaultRelayUrl = url;
        return true;
    }

    // Ohne Account gibt es kein Zurück.
    protected override bool OnBackButtonPressed() => true;

    async void OnStart(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameEntry.Text))
        {
            await DisplayAlertAsync("Name fehlt", "Bitte gib einen Namen ein.", "OK");
            return;
        }
        if (!await ApplyServerAsync()) return;
        await RunBusy(async () =>
        {
            await _account.CreateAsync(NameEntry.Text);
            await Shell.Current.GoToAsync("..");
        });
    }

    async void OnRestore(object? sender, EventArgs e)
    {
        if (!await ApplyServerAsync()) return;
        await RunBusy(async () =>
        {
            try
            {
                if (await _account.RestoreAsync(CodeEntry.Text ?? ""))
                    await Shell.Current.GoToAsync("..");
                else
                    await DisplayAlertAsync("Nicht gefunden", "Zu diesem Code gibt es kein Backup.", "OK");
            }
            catch (FormatException)
            {
                await DisplayAlertAsync("Ungültiger Code", "Der Code besteht aus 25 Zeichen, z. B. ABCDE-12345-…", "OK");
            }
            catch (HttpRequestException)
            {
                await DisplayAlertAsync("Keine Verbindung", $"{AccountService.DefaultRelayUrl} ist gerade nicht erreichbar.", "OK");
            }
        });
    }

    async Task RunBusy(Func<Task> action)
    {
        StartButton.IsEnabled = RestoreButton.IsEnabled = false;
        Busy.IsRunning = true;
        try { await action(); }
        finally
        {
            StartButton.IsEnabled = RestoreButton.IsEnabled = true;
            Busy.IsRunning = false;
        }
    }
}
