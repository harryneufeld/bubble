using Bubble.App.Controls;
using Bubble.App.Services;

namespace Bubble.App.Pages;

public partial class ProfilePage : ContentPage
{
    readonly AccountService _account;
    readonly FeedService _feed;
    readonly PushService _push;
    bool _settingSwitch;

    public ProfilePage(AccountService account, FeedService feed, PushService push)
    {
        InitializeComponent();
        _account = account;
        _feed = feed;
        _push = push;
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
        StatsLabel.Text = vault.Bubbles.Count == 1 ? "In 1 bubble" : $"In {vault.Bubbles.Count} bubbles";
        BackupLabel.Text = vault.Backup is null ? "Not set up yet" : "Active, updated automatically";
        ServerLabel.Text = Labels.Host(AccountService.DefaultRelayUrl);
        NotificationsRow.IsVisible = NotificationsDivider.IsVisible = PushService.IsAvailable;
        _settingSwitch = true;
        NotificationsSwitch.IsToggled = PushService.IsEnabled;
        _settingSwitch = false;
        VersionLabel.Text = $"Version {AppInfo.Current.VersionString} ({AppInfo.Current.BuildString})";
    }

    async void OnGitHub(object? sender, TappedEventArgs e) => await OpenAsync("https://github.com/harryneufeld/bubble");

    async void OnWebsite(object? sender, TappedEventArgs e) => await OpenAsync("https://harry-neufeld.de");

    async Task OpenAsync(string url)
    {
        try
        {
            if (!await Launcher.Default.OpenAsync(new Uri(url)))
                await DisplayAlertAsync("Link not opened", $"Please open {url} in your browser yourself.", "OK");
        }
        catch (Exception)
        {
            // no browser found or not allowed
            await DisplayAlertAsync("Link not opened", $"Please open {url} in your browser yourself.", "OK");
        }
    }

    async void OnEdit(object? sender, EventArgs e) => await Shell.Current.GoToAsync(nameof(ProfileEditPage));

    async void OnBackup(object? sender, TappedEventArgs e) => await Shell.Current.GoToAsync(nameof(BackupPage));

    async void OnServer(object? sender, TappedEventArgs e)
    {
        var input = await SheetPage.PromptAsync(this, "Server",
            "Applies to new bubbles and your backup. Existing bubbles stay on their server.",
            "Save", "192.168.1.20:5080", Labels.Host(AccountService.DefaultRelayUrl), 100, Keyboard.Url);
        if (input is null) return;
        if (!AccountService.TryNormalizeRelayUrl(input, out var url))
        {
            await DisplayAlertAsync("Invalid server address", "Example: 192.168.1.20:5080", "OK");
            return;
        }
        if (!await AccountService.IsReachableAsync(url))
        {
            await DisplayAlertAsync("Server not reachable", $"No Bubble relay answers at {Labels.Host(url)}.", "OK");
            return;
        }
        AccountService.DefaultRelayUrl = url;
        var backupOk = await _account.SaveAsync(); // uploads an existing backup to the new server
        ServerLabel.Text = Labels.Host(url);
        if (!backupOk)
            await DisplayAlertAsync("Backup not updated", "The server is not reachable. The backup will be updated on the next save.", "OK");
    }

    async void OnNotificationsToggled(object? sender, ToggledEventArgs e)
    {
        if (_settingSwitch) return;
        if (await _push.SetEnabledAsync(e.Value)) return;

        _settingSwitch = true;
        NotificationsSwitch.IsToggled = PushService.IsEnabled;
        _settingSwitch = false;
        if (e.Value)
            await DisplayAlertAsync("Notifications not switched on",
                "Check that Bubble may show notifications and that your server and its push gateway are reachable. " +
                "Servers that don't offer push can't notify you yet.", "OK");
    }

    async void OnLogOut(object? sender, TappedEventArgs e)
    {
        var vault = _account.Vault;
        if (vault is null) return;
        var message = vault.Backup is null
            ? "You have not set up a backup. Logging out deletes your identity and all bubble keys from this device for good – you will lose access to your bubbles."
            : "Your identity and bubble keys are removed from this device. You can restore everything later with your backup code, so make sure you still have it.";
        if (!await DisplayAlertAsync("Log out?", message, "Log out", "Cancel")) return;

        await _push.RemoveAllAsync(); // before the account goes: the relays should forget this device
        _account.LogOut();
        _feed.Reset();
        await Shell.Current.GoToAsync("//home"); // the home page notices there is no account and shows onboarding
    }
}
