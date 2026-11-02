using Bubble.App.Controls;
using Bubble.App.Services;
using Bubble.Core;

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
        PostsSwitch.IsToggled = NotificationPreferences.IsOn(NotificationKind.Post);
        HeartsSwitch.IsToggled = NotificationPreferences.IsOn(NotificationKind.Heart);
        CommentsSwitch.IsToggled = NotificationPreferences.IsOn(NotificationKind.Comment);
        NotificationKindsList.IsVisible = PushService.IsEnabled;
        _settingSwitch = false;
        VersionLabel.Text = $"Version {AppInfo.Current.VersionString}, credits and links";
        ShowHomeMode();
    }

    async void OnAbout(object? sender, TappedEventArgs e) => await Shell.Current.GoToAsync(nameof(AboutPage));

    void OnClassicMode(object? sender, TappedEventArgs e) => SetHomeMode(HomeLayout.Classic);

    void OnBoxMode(object? sender, TappedEventArgs e) => SetHomeMode(HomeLayout.Box);

    void SetHomeMode(HomeLayout layout)
    {
        HomeMode.Current = layout;
        ShowHomeMode();
    }

    void ShowHomeMode()
    {
        var box = HomeMode.Current == HomeLayout.Box;
        Mark(ClassicOption, "Classic", !box);
        Mark(BoxOption, "Box of memories", box);
    }

    static void Mark(Border option, string name, bool selected)
    {
        option.Stroke = (Color)Application.Current!.Resources[selected ? "Accent" : "Hairline"];
        option.StrokeThickness = selected ? 2 : 1;
        SemanticProperties.SetDescription(option, selected ? $"Home screen: {name}, selected" : $"Home screen: {name}");
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
            await this.ShowAlertAsync("Invalid server address", "Example: 192.168.1.20:5080", "OK");
            return;
        }
        if (!await AccountService.IsReachableAsync(url))
        {
            await this.ShowAlertAsync("Server not reachable", $"No Bubble relay answers at {Labels.Host(url)}.", "OK");
            return;
        }
        AccountService.DefaultRelayUrl = url;
        var backupOk = await _account.SaveAsync(); // uploads an existing backup to the new server
        ServerLabel.Text = Labels.Host(url);
        if (!backupOk)
            await this.ShowAlertAsync("Backup not updated", "The server is not reachable. The backup will be updated on the next save.", "OK");
    }

    void OnKindToggled(object? sender, ToggledEventArgs e)
    {
        if (_settingSwitch) return;
        var kind = sender == HeartsSwitch ? NotificationKind.Heart
            : sender == CommentsSwitch ? NotificationKind.Comment
            : NotificationKind.Post;
        NotificationPreferences.Set(kind, e.Value);
    }

    async void OnNotificationsToggled(object? sender, ToggledEventArgs e)
    {
        if (_settingSwitch) return;
        var ok = await _push.SetEnabledAsync(e.Value);
        NotificationKindsList.IsVisible = PushService.IsEnabled;
        if (ok) return;

        _settingSwitch = true;
        NotificationsSwitch.IsToggled = PushService.IsEnabled;
        _settingSwitch = false;
        if (e.Value)
            await this.ShowAlertAsync("Notifications not switched on",
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
        if (!await this.ShowAlertAsync("Log out?", message, "Log out", "Cancel")) return;

        await _push.RemoveAllAsync(); // before the account goes: the relays should forget this device
        _account.LogOut();
        _feed.Reset();
        await Shell.Current.GoToAsync("//home"); // the home page notices there is no account and shows onboarding
    }
}
