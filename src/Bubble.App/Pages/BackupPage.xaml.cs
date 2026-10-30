using Bubble.App.Services;

namespace Bubble.App.Pages;

public partial class BackupPage : ContentPage
{
    readonly AccountService _account;

    public BackupPage(AccountService account)
    {
        InitializeComponent();
        _account = account;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        var active = _account.Vault?.Backup is not null;
        StatusLabel.Text = active ? "✅ Backup is active and updated automatically." : "⚠️ No backup set up yet.";
        CreateButton.Text = active ? "Generate new backup code" : "Create backup code";
    }

    async void OnCreate(object? sender, EventArgs e)
    {
        if (_account.Vault?.Backup is not null &&
            !await DisplayAlertAsync("New code", "New changes only go into the new backup. The old code will only open the previous state. Continue?", "Yes", "Cancel"))
            return;

        CreateButton.IsEnabled = false;
        Busy.IsRunning = true;
        try
        {
            CodeLabel.Text = await _account.EnableBackupAsync();
            CodeBox.IsVisible = true;
            StatusLabel.Text = "✅ Backup is active and updated automatically.";
            CreateButton.IsVisible = false;
        }
        catch (HttpRequestException)
        {
            await DisplayAlertAsync("No connection", "The backup could not be uploaded.", "OK");
        }
        finally
        {
            CreateButton.IsEnabled = true;
            Busy.IsRunning = false;
        }
    }

    async void OnCopy(object? sender, EventArgs e)
    {
        await Clipboard.Default.SetTextAsync(CodeLabel.Text);
        await DisplayAlertAsync("Copied", "Paste the code into your password manager now and clear the clipboard afterwards.", "OK");
    }
}
