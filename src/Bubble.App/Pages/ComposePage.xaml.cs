using Bubble.App.Services;
using Bubble.Core;

namespace Bubble.App.Pages;

/// <summary>Highlight teilen – auch in mehrere Bubbles gleichzeitig.</summary>
[QueryProperty(nameof(MailboxId), "id")]
public partial class ComposePage : ContentPage
{
    readonly AccountService _account;
    readonly FeedService _feed;
    readonly HashSet<string> _selected = [];
    string? _emoji;
    byte[]? _image;
    bool _initialized;

    /// <summary>Optional: preselected bubble.</summary>
    public string MailboxId { get; set; } = "";

    public ComposePage(AccountService account, FeedService feed)
    {
        InitializeComponent();
        _account = account;
        _feed = feed;
        BuildFeelings();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (_initialized) return;
        _initialized = true;

        var bubbles = _account.Vault!.Bubbles;
        if (_account.FindBubble(MailboxId) is { } preset) _selected.Add(preset.MailboxId);
        else if (bubbles.Count == 1) _selected.Add(bubbles[0].MailboxId);
        BuildChips();

        if (ShareInbox.Take() is { } shared)
        {
            if (!string.IsNullOrWhiteSpace(shared.Text)) TextEditor.Text = shared.Text;
            if (shared.Image is { } image) _ = SetImageAsync(new MemoryStream(image));
        }
    }

    void BuildChips()
    {
        BubbleChips.Children.Clear();
        foreach (var bubble in _account.Vault!.Bubbles)
        {
            var on = _selected.Contains(bubble.MailboxId);
            var chip = new Border
            {
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 999 },
                Padding = new Thickness(14, 9), Margin = new Thickness(0, 0, 8, 8),
                Background = on ? (Brush)Application.Current!.Resources["CtaGradient"] : new SolidColorBrush((Color)Application.Current!.Resources["SurfaceRaised"]),
                Content = new Label
                {
                    Text = bubble.Name, FontFamily = "BodySemibold", FontSize = 14,
                    TextColor = on ? Colors.White : (Color)Application.Current.Resources["InkSoft"],
                },
            };
            SemanticProperties.SetDescription(chip, bubble.Name);
            SemanticProperties.SetHint(chip, on ? "selected" : "");
            var id = bubble.MailboxId;
            chip.GestureRecognizers.Add(new TapGestureRecognizer
            {
                Command = new Command(() =>
                {
                    if (!_selected.Add(id)) _selected.Remove(id);
                    BuildChips();
                }),
            });
            BubbleChips.Children.Add(chip);
        }
        UpdateHints();
    }

    void UpdateHints()
    {
        var chosen = _account.Vault!.Bubbles.Where(b => _selected.Contains(b.MailboxId)).ToList();
        LifetimeLabel.Text = chosen.Count switch
        {
            0 => "Choose where you want to share your highlight.",
            1 => chosen[0].Lifetime == PostLifetime.Story24h
                ? $"⏳ Your highlight disappears from “{chosen[0].Name}” after 24 hours."
                : $"📌 Your highlight stays in “{chosen[0].Name}” permanently.",
            var n => $"Goes to {n} bubbles. ⏳ Depending on the bubble it disappears after 24 hours or stays permanently.",
        };
        ShareButton.IsEnabled = chosen.Count > 0;
        ShareButton.Text = chosen.Count switch { 0 => "Choose a bubble", 1 => "Share", var n => $"Share in {n} bubbles" };
    }

    /// <summary>Emoji tiles with labels. Tapping the chosen emoji again clears the selection.</summary>
    void BuildFeelings()
    {
        FeelingGrid.Children.Clear();
        foreach (var (emoji, label) in Labels.Feelings)
        {
            var on = _emoji == emoji;
            var tile = new Border
            {
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 20 },
                WidthRequest = 78, HeightRequest = 78, Margin = new Thickness(0, 0, 8, 8),
                BackgroundColor = (Color)Application.Current!.Resources[on ? "SurfaceRaised" : "Surface"],
                StrokeThickness = on ? 2.5 : 0,
                Stroke = on ? (Brush)Application.Current.Resources["BubbleGradient"] : null,
                Content = new VerticalStackLayout
                {
                    Spacing = 2, VerticalOptions = LayoutOptions.Center,
                    Children =
                    {
                        new Label { Text = emoji, FontSize = on ? 32 : 28, HorizontalOptions = LayoutOptions.Center },
                        new Label
                        {
                            Text = label, FontSize = 11, FontFamily = "BodyMedium", HorizontalOptions = LayoutOptions.Center,
                            TextColor = (Color)Application.Current.Resources[on ? "Ink" : "InkSoft"],
                        },
                    },
                },
            };
            SemanticProperties.SetDescription(tile, label);
            SemanticProperties.SetHint(tile, on ? "selected" : "");
            var picked = emoji;
            tile.GestureRecognizers.Add(new TapGestureRecognizer
            {
                Command = new Command(() =>
                {
                    _emoji = _emoji == picked ? null : picked;
                    BuildFeelings();
                }),
            });
            FeelingGrid.Children.Add(tile);
        }
        FeelingHint.Text = _emoji is null
            ? "Tap an emoji that fits your day."
            : $"You feel {Labels.Feelings.First(f => f.Emoji == _emoji).Label.ToLowerInvariant()}.";
    }

    async void OnPickPhoto(object? sender, EventArgs e)
    {
        var photo = await PhotoPicker.PickAsync(this, "Add photo");
        if (photo is null) return;
        await using var stream = await photo.OpenReadAsync();
        await SetImageAsync(stream);
    }

    async Task SetImageAsync(Stream stream)
    {
        try
        {
            ShowImage(await ImageSanitizer.SanitizeAsync(stream), designed: false);
        }
        catch (InvalidDataException)
        {
            await DisplayAlertAsync("Photo not readable", "Unfortunately this image format is not supported.", "OK");
        }
    }

    /// <summary>A design is shown whole; a photo fills the preview like in the feed.</summary>
    void ShowImage(byte[] bytes, bool designed)
    {
        _image = bytes;
        Preview.Source = ImageSource.FromStream(() => new MemoryStream(bytes));
        Preview.Aspect = designed ? Aspect.AspectFit : Aspect.AspectFill;
        PreviewFrame.IsVisible = RemovePhotoButton.IsVisible = true;
        PhotoButton.Text = "Change photo";
        DesignButton.Text = "Edit design";
    }

    /// <summary>Opens the editor; a chosen photo comes along as the starting point.</summary>
    async void OnDesign(object? sender, EventArgs e)
    {
        if (await EditorPage.OpenAsync(this, _image) is { } jpeg) ShowImage(jpeg, designed: true);
    }

    void OnRemovePhoto(object? sender, EventArgs e)
    {
        _image = null;
        Preview.Source = null;
        PreviewFrame.IsVisible = RemovePhotoButton.IsVisible = false;
        PhotoButton.Text = "Add photo";
        DesignButton.Text = "Design";
    }

    async void OnShare(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TextEditor.Text) && _image is null)
        {
            await DisplayAlertAsync("Nothing yet", "Write something or add a photo.", "OK");
            return;
        }

        ShareButton.IsEnabled = false;
        Busy.IsRunning = true;
        var failed = new List<string>();
        try
        {
            foreach (var bubble in _account.Vault!.Bubbles.Where(b => _selected.Contains(b.MailboxId)))
            {
                try { await _account.Client.PostAsync(bubble, _emoji, TextEditor.Text, _image); }
                catch (HttpRequestException ex) { failed.Add($"“{bubble.Name}”: {Labels.ConnectionError(bubble.RelayUrl, ex)}"); }
            }
            if (failed.Count > 0)
                await DisplayAlertAsync("Not shared everywhere", string.Join("\n", failed), "OK");
            await _feed.RefreshAsync();
            if (failed.Count < _selected.Count) await Shell.Current.GoToAsync("..");
        }
        finally
        {
            Busy.IsRunning = false;
            UpdateHints();
        }
    }
}
