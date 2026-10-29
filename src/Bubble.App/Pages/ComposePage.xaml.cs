using Bubble.App.Services;
using Bubble.Core;

namespace Bubble.App.Pages;

/// <summary>Ein Highlight pro Tag und Bubble – hier lässt sich in mehrere Bubbles gleichzeitig teilen.</summary>
[QueryProperty(nameof(MailboxId), "id")]
public partial class ComposePage : ContentPage
{
    readonly AccountService _account;
    readonly FeedService _feed;
    readonly HashSet<string> _selected = [];
    Mood _mood = Mood.Top;
    byte[]? _image;
    bool _initialized;

    /// <summary>Optional: vorausgewählte Bubble.</summary>
    public string MailboxId { get; set; } = "";

    public ComposePage(AccountService account, FeedService feed)
    {
        InitializeComponent();
        _account = account;
        _feed = feed;
        ShowMood();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (_initialized) return;
        _initialized = true;

        var open = _account.Vault!.Bubbles.Where(b => !_feed.HasPostedToday(b)).ToList();
        if (_account.FindBubble(MailboxId) is { } preset && open.Contains(preset)) _selected.Add(preset.MailboxId);
        else if (open.Count == 1) _selected.Add(open[0].MailboxId);
        BuildChips();
    }

    void BuildChips()
    {
        BubbleChips.Children.Clear();
        foreach (var bubble in _account.Vault!.Bubbles)
        {
            var done = _feed.HasPostedToday(bubble);
            var on = _selected.Contains(bubble.MailboxId);
            var chip = new Border
            {
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 999 },
                Padding = new Thickness(14, 9), Margin = new Thickness(0, 0, 8, 8),
                Background = on ? (Brush)Application.Current!.Resources["CtaGradient"] : new SolidColorBrush((Color)Application.Current!.Resources["SurfaceRaised"]),
                Opacity = done ? 0.45 : 1,
                Content = new Label
                {
                    Text = done ? $"✓ {bubble.Name}" : bubble.Name, FontFamily = "BodySemibold", FontSize = 14,
                    TextColor = on ? Colors.White : (Color)Application.Current.Resources["InkSoft"],
                },
            };
            SemanticProperties.SetDescription(chip, done ? $"{bubble.Name}, heute schon geteilt" : bubble.Name);
            SemanticProperties.SetHint(chip, on ? "ausgewählt" : "");
            if (!done)
            {
                var id = bubble.MailboxId;
                chip.GestureRecognizers.Add(new TapGestureRecognizer
                {
                    Command = new Command(() =>
                    {
                        if (!_selected.Add(id)) _selected.Remove(id);
                        BuildChips();
                    }),
                });
            }
            BubbleChips.Children.Add(chip);
        }
        UpdateHints();
    }

    void UpdateHints()
    {
        var chosen = _account.Vault!.Bubbles.Where(b => _selected.Contains(b.MailboxId)).ToList();
        LifetimeLabel.Text = chosen.Count switch
        {
            0 => _account.Vault.Bubbles.All(_feed.HasPostedToday)
                ? "Du hast heute in allen Bubbles geteilt. Morgen wieder!"
                : "Wähle, wo du dein Highlight teilen möchtest.",
            1 => chosen[0].Lifetime == PostLifetime.Story24h
                ? $"⏳ Dein Highlight verschwindet nach 24 Stunden aus „{chosen[0].Name}“."
                : $"📌 Dein Highlight bleibt dauerhaft in „{chosen[0].Name}“.",
            var n => $"Geht an {n} Bubbles. ⏳ Je nach Bubble verschwindet es nach 24 Stunden oder bleibt dauerhaft.",
        };
        ShareButton.IsEnabled = chosen.Count > 0;
        ShareButton.Text = chosen.Count switch { 0 => "Wähle eine Bubble", 1 => "Teilen", var n => $"In {n} Bubbles teilen" };
    }

    void OnTop(object? sender, TappedEventArgs e) { _mood = Mood.Top; ShowMood(); }

    void OnFlop(object? sender, TappedEventArgs e) { _mood = Mood.Flop; ShowMood(); }

    void ShowMood()
    {
        ShowChoice(TopChoice, TopText, _mood == Mood.Top, "Top", "OnTop");
        ShowChoice(FlopChoice, FlopText, _mood == Mood.Flop, "Flop", "OnFlop");
    }

    static void ShowChoice(Border choice, Label text, bool selected, string fill, string ink)
    {
        Color Res(string key) => Application.Current!.Resources.TryGetValue(key, out var c) ? (Color)c : Colors.Transparent;
        choice.BackgroundColor = Res(selected ? fill : "Surface");
        choice.Stroke = new SolidColorBrush(Res(selected ? ink : "Hairline"));
        choice.StrokeThickness = selected ? 2 : 1;
        text.TextColor = Res(selected ? ink : "InkSoft");
        SemanticProperties.SetHint(choice, selected ? "ausgewählt" : "");
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
                try { await _account.Client.PostAsync(bubble, _mood, TextEditor.Text, _image); }
                catch (InvalidOperationException) { failed.Add($"„{bubble.Name}“: heute schon geteilt"); }
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
