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

    /// <summary>Optional: vorausgewählte Bubble.</summary>
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
            SemanticProperties.SetHint(chip, on ? "ausgewählt" : "");
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
            0 => "Wähle, wo du dein Highlight teilen möchtest.",
            1 => chosen[0].Lifetime == PostLifetime.Story24h
                ? $"⏳ Dein Highlight verschwindet nach 24 Stunden aus „{chosen[0].Name}“."
                : $"📌 Dein Highlight bleibt dauerhaft in „{chosen[0].Name}“.",
            var n => $"Geht an {n} Bubbles. ⏳ Je nach Bubble verschwindet es nach 24 Stunden oder bleibt dauerhaft.",
        };
        ShareButton.IsEnabled = chosen.Count > 0;
        ShareButton.Text = chosen.Count switch { 0 => "Wähle eine Bubble", 1 => "Teilen", var n => $"In {n} Bubbles teilen" };
    }

    /// <summary>Emoji-Kacheln mit Beschriftung. Ein weiterer Tipp auf das gewählte Emoji nimmt die Auswahl zurück.</summary>
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
            SemanticProperties.SetHint(tile, on ? "ausgewählt" : "");
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
            ? "Tippe ein Emoji, das zu deinem Tag passt."
            : $"Du fühlst dich {Labels.Feelings.First(f => f.Emoji == _emoji).Label.ToLowerInvariant()}.";
    }

    async void OnPickPhoto(object? sender, EventArgs e)
    {
        var photo = await PhotoPicker.PickAsync(this, "Foto hinzufügen");
        if (photo is null) return;
        try
        {
            await using var stream = await photo.OpenReadAsync();
            _image = await ImageSanitizer.SanitizeAsync(stream);
            var bytes = _image;
            Preview.Source = ImageSource.FromStream(() => new MemoryStream(bytes));
            PreviewFrame.IsVisible = RemovePhotoButton.IsVisible = true;
            PhotoButton.Text = "Foto ändern";
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
        PreviewFrame.IsVisible = RemovePhotoButton.IsVisible = false;
        PhotoButton.Text = "Foto hinzufügen";
    }

    async void OnShare(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TextEditor.Text) && _image is null)
        {
            await DisplayAlertAsync("Noch leer", "Schreib etwas oder füge ein Foto hinzu.", "OK");
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
                catch (HttpRequestException ex) { failed.Add($"„{bubble.Name}“: {Labels.ConnectionError(bubble.RelayUrl, ex)}"); }
            }
            if (failed.Count > 0)
                await DisplayAlertAsync("Nicht überall geteilt", string.Join("\n", failed), "OK");
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
