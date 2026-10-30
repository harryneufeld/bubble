using Bubble.App.Services;
using Bubble.Core;

namespace Bubble.App.Pages;

/// <summary>Invite via QR code (full link), short code (24 h, through the relay) or a shared link.</summary>
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
        TitleLabel.Text = $"Who should be in “{_bubble.Name}”?";
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
            CodeHint.Text = $"Valid for 24 hours · Server {Labels.Host(bubble.RelayUrl)}";
            CopyButton.IsEnabled = true;
        }
        catch (HttpRequestException ex)
        {
            CodeHint.Text = Labels.ConnectionError(bubble.RelayUrl, ex) + " The QR code and link still work.";
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
        CopyButton.Text = "✓ Copied";
    }

    async void OnShare(object? sender, EventArgs e)
    {
        if (_bubble is null) return;
        await Share.Default.RequestAsync(new ShareTextRequest
        {
            Title = $"Invitation to “{_bubble.Name}”",
            Text = Invite.For(_bubble).ToLink(),
        });
    }
}
