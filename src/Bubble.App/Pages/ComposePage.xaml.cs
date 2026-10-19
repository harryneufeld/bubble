using Bubble.App.Services;
using Bubble.Core;

namespace Bubble.App.Pages;

[QueryProperty(nameof(MailboxId), "id")]
public partial class ComposePage : ContentPage
{
    readonly AccountService _account;
    BubbleMembership? _bubble;
    Mood _mood = Mood.Top;
    byte[]? _image;

    public string MailboxId { get; set; } = "";

    public ComposePage(AccountService account)
    {
        InitializeComponent();
        _account = account;
        ShowMood();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _bubble = _account.FindBubble(MailboxId);
        if (_bubble is null) return;
        LifetimeLabel.Text = _bubble.Lifetime == PostLifetime.Story24h
            ? $"⏳ Dieser Post verschwindet nach 24 Stunden aus „{_bubble.Name}“."
            : $"📌 Dieser Post bleibt dauerhaft in „{_bubble.Name}“.";
    }

    void OnTop(object? sender, EventArgs e) { _mood = Mood.Top; ShowMood(); }

    void OnFlop(object? sender, EventArgs e) { _mood = Mood.Flop; ShowMood(); }

    void ShowMood()
    {
        TopButton.Opacity = _mood == Mood.Top ? 1 : 0.4;
        FlopButton.Opacity = _mood == Mood.Flop ? 1 : 0.4;
    }

    async void OnPickPhoto(object? sender, EventArgs e)
    {
        var photo = (await MediaPicker.Default.PickPhotosAsync(new MediaPickerOptions { SelectionLimit = 1 })).FirstOrDefault();
        if (photo is null) return;
        try
        {
            await using var stream = await photo.OpenReadAsync();
            _image = await ImageSanitizer.SanitizeAsync(stream);
            var bytes = _image;
            Preview.Source = ImageSource.FromStream(() => new MemoryStream(bytes));
            Preview.IsVisible = RemovePhotoButton.IsVisible = true;
        }
        catch (InvalidDataException)
        {
            await DisplayAlertAsync("Foto nicht lesbar", "Dieses Bildformat wird leider nicht unterstützt.", "OK");
        }
    }

    void OnRemovePhoto(object? sender, EventArgs e)
    {
        _image = null;
        Preview.Source = null;
        Preview.IsVisible = RemovePhotoButton.IsVisible = false;
    }

    async void OnShare(object? sender, EventArgs e)
    {
        if (_bubble is null) return;
        if (string.IsNullOrWhiteSpace(TextEditor.Text) && _image is null)
        {
            await DisplayAlertAsync("Noch leer", "Schreib etwas oder wähle ein Foto.", "OK");
            return;
        }

        ShareButton.IsEnabled = false;
        Busy.IsRunning = true;
        try
        {
            await _account.Client.PostAsync(_bubble, _mood, TextEditor.Text, _image);
            await Shell.Current.GoToAsync("..");
        }
        catch (InvalidOperationException ex)
        {
            await DisplayAlertAsync("Schon geteilt", ex.Message, "OK");
        }
        catch (HttpRequestException ex)
        {
            await DisplayAlertAsync("Nicht geteilt", Labels.ConnectionError(_bubble.RelayUrl, ex), "OK");
        }
        finally
        {
            ShareButton.IsEnabled = true;
            Busy.IsRunning = false;
        }
    }
}
