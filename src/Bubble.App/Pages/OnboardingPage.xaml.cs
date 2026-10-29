using Bubble.App.Controls;
using Bubble.App.Services;

namespace Bubble.App.Pages;

public partial class OnboardingPage : ContentPage
{
    readonly AccountService _account;
    byte[]? _avatar;

    public OnboardingPage(AccountService account)
    {
        InitializeComponent();
        _account = account;
        ShowServer();
    }

    // Ohne Account gibt es kein Zurück.
    protected override bool OnBackButtonPressed()
    {
        if (!WelcomeStep.IsVisible) OnShowWelcome(null, EventArgs.Empty);
        return true;
    }

    void OnBegin(object? sender, EventArgs e) => ShowStep(ProfileStep);
    void OnShowRestore(object? sender, EventArgs e) => ShowStep(RestoreStep);
    void OnShowWelcome(object? sender, EventArgs e) => ShowStep(WelcomeStep);

    async void ShowStep(View step)
    {
        foreach (var s in new View[] { WelcomeStep, ProfileStep, RestoreStep }) s.IsVisible = s == step;
        step.Opacity = 0;
        step.TranslationY = 24;
        await Task.WhenAll(step.FadeToAsync(1, 250, Easing.CubicOut), step.TranslateToAsync(0, 0, 250, Easing.CubicOut));
    }

    void OnNameChanged(object? sender, TextChangedEventArgs e) => AvatarPreview.Name = e.NewTextValue ?? "";

    async void OnPickAvatar(object? sender, TappedEventArgs e)
    {
        var photo = await PhotoPicker.PickAsync(this, "Profilbild", _avatar is not null, () =>
        {
            _avatar = null;
            AvatarPreview.Image = null;
        });
        if (photo is null) return;
        try
        {
            await using var stream = await photo.OpenReadAsync();
            _avatar = await ImageSanitizer.SanitizeAvatarAsync(stream);
            AvatarPreview.Image = _avatar;
        }
        catch (InvalidDataException)
        {
            await DisplayAlertAsync("Foto nicht lesbar", "Dieses Bildformat wird leider nicht unterstützt.", "OK");
        }
    }

    void ShowServer() => ServerLabel.Text = $"Server: {Labels.Host(AccountService.DefaultRelayUrl)}";

    async void OnChangeServer(object? sender, EventArgs e)
    {
        var input = await SheetPage.PromptAsync(this, "Server",
            "Adresse des Relays, z. B. eines Rechners in deinem Netzwerk. Er speichert nur verschlüsselte Daten.",
            "Speichern", "192.168.1.20:5080", Labels.Host(AccountService.DefaultRelayUrl), 100, Keyboard.Url);
        if (input is null) return;
        if (await CheckServerAsync(input)) ShowServer();
    }

    async Task<bool> CheckServerAsync(string? input)
    {
        if (!AccountService.TryNormalizeRelayUrl(input, out var url))
        {
            await DisplayAlertAsync("Ungültige Server-Adresse", "Beispiel: 192.168.1.20:5080", "OK");
            return false;
        }
        if (!await AccountService.IsReachableAsync(url))
        {
            await DisplayAlertAsync("Server nicht erreichbar", $"Unter {Labels.Host(url)} antwortet kein Bubble-Relay. Prüfe die Adresse über „Server ändern“.", "OK");
            return false;
        }
        AccountService.DefaultRelayUrl = url;
        return true;
    }

    async void OnCreate(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameEntry.Text))
        {
            await DisplayAlertAsync("Name fehlt", "Wie sollen dich deine Leute nennen?", "OK");
            NameEntry.Focus();
            return;
        }
        await RunBusy(async () =>
        {
            if (!await CheckServerAsync(AccountService.DefaultRelayUrl)) return;
            await _account.CreateAsync(NameEntry.Text, BioEntry.Text, _avatar);
            await Shell.Current.GoToAsync("..");
        });
    }

    async void OnRestore(object? sender, EventArgs e)
    {
        await RunBusy(async () =>
        {
            if (!await CheckServerAsync(AccountService.DefaultRelayUrl)) return;
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
                await DisplayAlertAsync("Keine Verbindung", $"{Labels.Host(AccountService.DefaultRelayUrl)} ist gerade nicht erreichbar.", "OK");
            }
        });
    }

    async Task RunBusy(Func<Task> action)
    {
        CreateButton.IsEnabled = RestoreButton.IsEnabled = false;
        Busy.IsRunning = true;
        try { await action(); }
        finally
        {
            CreateButton.IsEnabled = RestoreButton.IsEnabled = true;
            Busy.IsRunning = false;
        }
    }
}
