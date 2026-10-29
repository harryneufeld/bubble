using Bubble.App.Services;
using Bubble.Core;

namespace Bubble.App.Pages;

/// <summary>Einladen per QR-Code (vollständiger Link), kurzem Code (24 h, über den Relay) oder geteiltem Link.</summary>
[QueryProperty(nameof(MailboxId), "id")]
public partial class InvitePage : ContentPage
{
    readonly AccountService _account;
    BubbleMembership? _bubble;
    string? _code;

    public string MailboxId { get; set; } = "";

    public InvitePage(AccountService account)
    {
        InitializeComponent();
        _account = account;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _bubble = _account.FindBubble(MailboxId);
        if (_bubble is null)
        {
            await Shell.Current.GoToAsync("..");
            return;
        }
        TitleLabel.Text = $"Wer soll in „{_bubble.Name}“?";
        QrImage.Source = QrCodes.Create(Invite.For(_bubble).ToLink());
        if (_code is null) await CreateCodeAsync();
    }

    async Task CreateCodeAsync()
    {
        var bubble = _bubble!;
        CodeBusy.IsRunning = CodeBusy.IsVisible = true;
        try
        {
            _code = await ShortInvite.PublishAsync(Invite.For(bubble), _account.RelayFor(bubble.RelayUrl));
            CodeLabel.Text = _code;
            CodeHint.Text = $"Gilt 24 Stunden · Server {Labels.Host(bubble.RelayUrl)}";
            CopyButton.IsEnabled = true;
        }
        catch (HttpRequestException ex)
        {
            CodeHint.Text = Labels.ConnectionError(bubble.RelayUrl, ex) + " QR-Code und Link funktionieren trotzdem.";
        }
        finally
        {
            CodeBusy.IsRunning = CodeBusy.IsVisible = false;
        }
    }

    async void OnCopy(object? sender, EventArgs e)
    {
        if (_code is null) return;
        await Clipboard.Default.SetTextAsync(_code);
        CopyButton.Text = "✓ Kopiert";
    }

    async void OnShare(object? sender, EventArgs e)
    {
        if (_bubble is null) return;
        await Share.Default.RequestAsync(new ShareTextRequest
        {
            Title = $"Einladung in „{_bubble.Name}“",
            Text = Invite.For(_bubble).ToLink(),
        });
    }
}
