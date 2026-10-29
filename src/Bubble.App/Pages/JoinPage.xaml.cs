using Bubble.App.Services;
using Bubble.Core;
using ZXing.Net.Maui;
using ZXing.Net.Maui.Controls;

namespace Bubble.App.Pages;

/// <summary>Beitreten per QR-Code, kurzem Code oder Link. Die Kamera läuft nur, solange der Scan-Tab offen ist.</summary>
public partial class JoinPage : ContentPage
{
    readonly AccountService _account;
    CameraBarcodeReaderView? _camera;
    Invite? _invite;

    public JoinPage(AccountService account)
    {
        InitializeComponent();
        _account = account;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _account.LoadAsync();
        await ShowScanAsync();
    }

    protected override void OnDisappearing()
    {
        StopCamera();
        base.OnDisappearing();
    }

    async void OnScanTab(object? sender, TappedEventArgs e) => await ShowScanAsync();

    void OnCodeTab(object? sender, TappedEventArgs e) => ShowCode();

    void SelectTab(bool scan)
    {
        ScanTab.Background = scan ? (Brush)Application.Current!.Resources["CtaGradient"] : null;
        CodeTab.Background = scan ? null : (Brush)Application.Current!.Resources["CtaGradient"];
        var soft = (Color)Application.Current!.Resources["InkSoft"];
        ScanTabText.TextColor = scan ? Colors.White : soft;
        CodeTabText.TextColor = scan ? soft : Colors.White;
        ScanPanel.IsVisible = scan;
        CodePanel.IsVisible = !scan;
    }

    async Task ShowScanAsync()
    {
        SelectTab(scan: true);
        if (_invite is not null || _camera is not null) return;

        if (await Permissions.RequestAsync<Permissions.Camera>() != PermissionStatus.Granted)
        {
            CameraMessage.Text = "Ohne Kamera-Zugriff kein Scannen. Du kannst den Code auch eingeben.";
            return;
        }
        try
        {
            _camera = new CameraBarcodeReaderView
            {
                Options = new BarcodeReaderOptions { Formats = BarcodeFormat.QrCode, AutoRotate = true, Multiple = false },
            };
            _camera.BarcodesDetected += OnDetected;
            CameraHost.Children.Add(_camera);
            CameraMessage.IsVisible = false;
        }
        catch (Exception ex) when (ex is NotSupportedException or NotImplementedException or InvalidOperationException)
        {
            CameraMessage.Text = "Auf diesem Gerät gibt es keine Kamera zum Scannen. Gib stattdessen den Code ein.";
            _camera = null;
        }
    }

    void ShowCode()
    {
        StopCamera();
        SelectTab(scan: false);
    }

    void StopCamera()
    {
        if (_camera is null) return;
        _camera.IsDetecting = false;
        _camera.BarcodesDetected -= OnDetected;
        CameraHost.Children.Remove(_camera);
        _camera.Handler?.DisconnectHandler();
        _camera = null;
        CameraMessage.IsVisible = true;
    }

    void OnDetected(object? sender, BarcodeDetectionEventArgs e)
    {
        var value = e.Results.FirstOrDefault()?.Value;
        if (value is null || !Invite.TryParse(value, out var invite)) return;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_invite is not null) return;
            StopCamera();
            CameraMessage.Text = "✓ QR-Code erkannt";
            ShowPreview(invite!);
        });
    }

    async void OnPaste(object? sender, EventArgs e)
    {
        var text = Clipboard.Default.HasText ? await Clipboard.Default.GetTextAsync() : null;
        if (text is not null && Invite.TryParse(text, out var invite)) ShowPreview(invite!);
        else if (text is not null && ShortInvite.TryNormalize(text, out var code)) { CodeEntry.Text = code; OnRedeem(null, EventArgs.Empty); }
        else CodeHint.Text = "In der Zwischenablage ist keine Bubble-Einladung.";
    }

    async void OnRedeem(object? sender, EventArgs e)
    {
        if (!ShortInvite.TryNormalize(CodeEntry.Text, out var code))
        {
            CodeHint.Text = "Der Code hat 10 Zeichen, z. B. 7KQ2M-9XH4T.";
            return;
        }
        CodeEntry.Text = code;
        RedeemButton.IsEnabled = false;
        Busy.IsRunning = true;
        CodeHint.Text = "";
        var relay = AccountService.DefaultRelayUrl;
        try
        {
            if (await ShortInvite.ResolveAsync(code, _account.RelayFor(relay)) is { } invite) ShowPreview(invite);
            else CodeHint.Text = $"Diesen Code gibt es auf {Labels.Host(relay)} nicht (mehr). Codes gelten 24 Stunden.";
        }
        catch (HttpRequestException ex)
        {
            CodeHint.Text = Labels.ConnectionError(relay, ex);
        }
        finally
        {
            RedeemButton.IsEnabled = true;
            Busy.IsRunning = false;
        }
    }

    void ShowPreview(Invite invite)
    {
        _invite = invite;
        PreviewName.Text = invite.Name;
        PreviewMeta.Text = $"{Labels.Lifetime(invite.Lifetime)} · Server {Labels.Host(invite.RelayUrl)}";
        Preview.IsVisible = true;
    }

    async void OnJoin(object? sender, EventArgs e)
    {
        if (_invite is null) return;
        var bubble = await _account.JoinAsync(_invite);
        if (bubble is null)
        {
            await DisplayAlertAsync("Schon dabei", $"Du bist bereits in „{_invite.Name}“.", "OK");
            return;
        }
        await Shell.Current.GoToAsync($"../{nameof(FeedPage)}?id={Uri.EscapeDataString(bubble.MailboxId)}");
    }
}
