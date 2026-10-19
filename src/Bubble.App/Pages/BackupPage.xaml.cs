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
        StatusLabel.Text = active ? "✅ Backup ist aktiv und wird automatisch aktualisiert." : "⚠️ Noch kein Backup eingerichtet.";
        CreateButton.Text = active ? "Neuen Backup-Code erzeugen" : "Backup-Code erstellen";
    }

    async void OnCreate(object? sender, EventArgs e)
    {
        if (_account.Vault?.Backup is not null &&
            !await DisplayAlertAsync("Neuer Code", "Neue Änderungen landen nur noch im neuen Backup. Der alte Code öffnet danach nur noch den bisherigen Stand. Fortfahren?", "Ja", "Abbrechen"))
            return;

        CreateButton.IsEnabled = false;
        Busy.IsRunning = true;
        try
        {
            CodeLabel.Text = await _account.EnableBackupAsync();
            CodeBox.IsVisible = true;
            StatusLabel.Text = "✅ Backup ist aktiv und wird automatisch aktualisiert.";
            CreateButton.IsVisible = false;
        }
        catch (HttpRequestException)
        {
            await DisplayAlertAsync("Keine Verbindung", "Das Backup konnte nicht hochgeladen werden.", "OK");
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
        await DisplayAlertAsync("Kopiert", "Füge den Code jetzt in deinen Passwortmanager ein und leere danach die Zwischenablage.", "OK");
    }
}
