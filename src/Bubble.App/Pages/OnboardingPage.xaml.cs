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

    // Without an account there is no way back.
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
        var photo = await PhotoPicker.PickAsync(this, "Profile picture", _avatar is not null, () =>
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
            await DisplayAlertAsync("Photo not readable", "Unfortunately this image format is not supported.", "OK");
        }
    }

    void ShowServer() => ServerLabel.Text = RestoreServerLabel.Text = $"Server: {Labels.Host(AccountService.DefaultRelayUrl)}";

    async void OnChangeServer(object? sender, EventArgs e)
    {
        var input = await SheetPage.PromptAsync(this, "Server",
            "Address of the relay, e.g. a computer on your network. It only stores encrypted data.",
            "Save", "192.168.1.20:5080", Labels.Host(AccountService.DefaultRelayUrl), 100, Keyboard.Url);
        if (input is null) return;
        if (await CheckServerAsync(input)) ShowServer();
    }

    async Task<bool> CheckServerAsync(string? input)
    {
        if (!AccountService.TryNormalizeRelayUrl(input, out var url))
        {
            await DisplayAlertAsync("Invalid server address", "Example: 192.168.1.20:5080", "OK");
            return false;
        }
        if (!await AccountService.IsReachableAsync(url))
        {
            await DisplayAlertAsync("Server not reachable", $"No Bubble relay answers at {Labels.Host(url)}. Check the address under “Change server”.", "OK");
            return false;
        }
        AccountService.DefaultRelayUrl = url;
        return true;
    }

    async void OnCreate(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameEntry.Text))
        {
            await DisplayAlertAsync("Name missing", "What should your people call you?", "OK");
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
                    await DisplayAlertAsync("Not found", $"There is no backup for this code on {Labels.Host(AccountService.DefaultRelayUrl)}. If your account was on another server, change the server first.", "OK");
            }
            catch (FormatException)
            {
                await DisplayAlertAsync("Invalid code", "The code has 25 characters, e.g. ABCDE-12345-…", "OK");
            }
            catch (HttpRequestException)
            {
                await DisplayAlertAsync("No connection", $"{Labels.Host(AccountService.DefaultRelayUrl)} is not reachable right now.", "OK");
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
