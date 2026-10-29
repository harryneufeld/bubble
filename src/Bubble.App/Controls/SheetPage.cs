using Microsoft.Maui.Controls.PlatformConfiguration;
using Microsoft.Maui.Controls.PlatformConfiguration.iOSSpecific;
using Microsoft.Maui.Controls.Shapes;
using Entry = Microsoft.Maui.Controls.Entry;
using Page = Microsoft.Maui.Controls.Page;
using Application = Microsoft.Maui.Controls.Application;
using ScrollView = Microsoft.Maui.Controls.ScrollView;

namespace Bubble.App.Controls;

/// <summary>Eine Zeile in einem Auswahl-Sheet: Emoji-Symbol, Titel, optional Erklärung.</summary>
public sealed record SheetOption(string Title, string? Description = null, string? Icon = null, bool Selected = false, bool Destructive = false);

/// <summary>
/// Bottom Sheet statt System-Dialog (DESIGN.md §6): dunkle Fläche mit Griff, Abdunklung dahinter,
/// große Auswahlzeilen oder ein Eingabefeld. Wird als transparente Modal-Seite gezeigt.
/// </summary>
public sealed class SheetPage : ContentPage
{
    readonly TaskCompletionSource<object?> _result = new();
    readonly Grid _root = new() { BackgroundColor = Colors.Transparent };
    readonly BoxView _scrim = new() { Color = Color.FromArgb("#99000000"), Opacity = 0 };
    readonly Border _sheet;
    readonly Entry? _entry;
    bool _closing;

    public Task<object?> Result => _result.Task;

    SheetPage(string title, string? message, View body, Entry? entry)
    {
        _entry = entry;
        BackgroundColor = Colors.Transparent;
        Shell.SetNavBarIsVisible(this, false);
        Shell.SetTabBarIsVisible(this, false);
        On<iOS>().SetModalPresentationStyle(UIModalPresentationStyle.OverFullScreen);

        var content = new VerticalStackLayout { Spacing = 12 };
        content.Add(new BoxView
        {
            Color = Res<Color>("Hairline"), WidthRequest = 40, HeightRequest = 4, CornerRadius = 2,
            HorizontalOptions = LayoutOptions.Center, Margin = new Thickness(0, 0, 0, 8),
        });
        content.Add(new Label { Text = title, FontFamily = "BodyBold", FontSize = 22, TextColor = Res<Color>("Ink") });
        if (!string.IsNullOrWhiteSpace(message))
            content.Add(new Label { Text = message, FontSize = 15, LineHeight = 1.3, TextColor = Res<Color>("InkSoft") });
        content.Add(body);

        _sheet = new Border
        {
            BackgroundColor = Res<Color>("Surface"),
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(28, 28, 0, 0) },
            Padding = new Thickness(20, 12, 20, 24),
            VerticalOptions = LayoutOptions.End,
            HorizontalOptions = LayoutOptions.Center,
            Content = new ScrollView { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Never },
        };
        SemanticProperties.SetDescription(_sheet, title);

        _scrim.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => _ = CloseAsync(null)) });
        _root.Children.Add(_scrim);
        _root.Children.Add(_sheet);
        Content = _root;

        // Von Anfang an unten außerhalb des Bildes: Setzt man das erst in OnAppearing, blitzt das Sheet für ein Bild an seinem Platz auf.
        _sheet.TranslationY = 2000;

        // auf breiten Fenstern höchstens 560 breit
        SizeChanged += (_, _) => _sheet.WidthRequest = Math.Min(560, Width);
    }

    static T Res<T>(string key) => (T)Application.Current!.Resources[key];

    /// <summary>Auswahl: Index der gewählten Option oder null bei Abbruch.</summary>
    public static async Task<int?> ChooseAsync(Page host, string title, string? message, IReadOnlyList<SheetOption> options, string? cancelText = "Abbrechen")
    {
        SheetPage? sheet = null;
        var list = new VerticalStackLayout { Spacing = 10, Margin = new Thickness(0, 8, 0, 0) };
        for (var i = 0; i < options.Count; i++)
        {
            var index = i;
            list.Add(OptionRow(options[i], () => _ = sheet!.CloseAsync(index)));
        }
        if (cancelText is not null)
            list.Add(Ghost(cancelText, () => _ = sheet!.CloseAsync(null)));

        sheet = new SheetPage(title, message, list, null);
        await host.Navigation.PushModalAsync(sheet, false);
        return await sheet.Result as int?;
    }

    /// <summary>Texteingabe: der eingegebene Text oder null bei Abbruch.</summary>
    public static async Task<string?> PromptAsync(Page host, string title, string? message, string confirmText,
        string? placeholder = null, string? initialValue = null, int maxLength = 80, Keyboard? keyboard = null)
    {
        SheetPage? sheet = null;
        var entry = new Entry
        {
            Placeholder = placeholder, Text = initialValue, MaxLength = maxLength, Keyboard = keyboard ?? Keyboard.Text,
            ReturnType = ReturnType.Done, VerticalOptions = LayoutOptions.Center,
        };
        SemanticProperties.SetDescription(entry, title);
        var field = new Border
        {
            Style = (Style)Application.Current!.Resources["Field"], Content = entry, Margin = new Thickness(0, 8, 0, 0),
        };
        var confirm = new Button { Text = confirmText };
        void Submit() => _ = sheet!.CloseAsync(entry.Text ?? "");
        confirm.Clicked += (_, _) => Submit();
        entry.Completed += (_, _) => Submit();

        var body = new VerticalStackLayout { Spacing = 10 };
        body.Add(field);
        body.Add(confirm);
        body.Add(Ghost("Abbrechen", () => _ = sheet!.CloseAsync(null)));

        sheet = new SheetPage(title, message, body, entry);
        await host.Navigation.PushModalAsync(sheet, false);
        return await sheet.Result as string;
    }

    static View OptionRow(SheetOption option, Action onTap)
    {
        var selected = option.Selected;
        var grid = new Grid { ColumnDefinitions = [new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto)], ColumnSpacing = 14 };

        var icon = new Border
        {
            StrokeShape = new Ellipse(), WidthRequest = 44, HeightRequest = 44, BackgroundColor = Res<Color>("Surface"),
            VerticalOptions = LayoutOptions.Center,
            Content = new Label { Text = option.Icon ?? "•", FontSize = 20, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center },
        };
        var texts = new VerticalStackLayout { Spacing = 2, VerticalOptions = LayoutOptions.Center };
        texts.Add(new Label
        {
            Text = option.Title, FontFamily = "BodySemibold", FontSize = 16,
            TextColor = option.Destructive ? Res<Color>("Error") : Res<Color>("Ink"),
        });
        if (option.Description is not null)
            texts.Add(new Label { Text = option.Description, FontSize = 13, TextColor = Res<Color>("InkSoft"), LineHeight = 1.25 });

        grid.Add(icon, 0);
        grid.Add(texts, 1);
        if (selected)
        {
            grid.Add(new Border
            {
                StrokeShape = new Ellipse(), WidthRequest = 26, HeightRequest = 26, Background = Res<Brush>("CtaGradient"),
                VerticalOptions = LayoutOptions.Center,
                Content = new Label { Text = "✓", TextColor = Colors.White, FontSize = 14, FontFamily = "BodyBold", HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center },
            }, 2);
        }

        var row = new Border
        {
            BackgroundColor = Res<Color>("SurfaceRaised"),
            StrokeShape = new RoundRectangle { CornerRadius = 20 },
            StrokeThickness = selected ? 2 : 0,
            Stroke = selected ? Res<Brush>("BubbleGradient") : null,
            Padding = new Thickness(14, 12),
            Content = grid,
        };
        SemanticProperties.SetDescription(row, option.Description is null ? option.Title : $"{option.Title}. {option.Description}");
        SemanticProperties.SetHint(row, selected ? "ausgewählt" : "");
        row.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(async () =>
            {
                await row.ScaleToAsync(0.97, 70);
                await row.ScaleToAsync(1, 70);
                onTap();
            }),
        });
        return row;
    }

    static Button Ghost(string text, Action onTap)
    {
        var b = new Button { Text = text, Style = (Style)Application.Current!.Resources["GhostButton"] };
        b.Clicked += (_, _) => onTap();
        return b;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await Task.WhenAll(
            _scrim.FadeToAsync(1, 200),
            _sheet.TranslateToAsync(0, 0, 260, Easing.CubicOut));
        _entry?.Focus();
    }

    protected override bool OnBackButtonPressed()
    {
        _ = CloseAsync(null);
        return true;
    }

    async Task CloseAsync(object? result)
    {
        if (_closing) return;
        _closing = true;
        _entry?.Unfocus();
        await Task.WhenAll(
            _scrim.FadeToAsync(0, 160),
            _sheet.TranslateToAsync(0, Math.Max(_sheet.Height, 300), 200, Easing.CubicIn));
        await Navigation.PopModalAsync(false);
        _result.TrySetResult(result);
    }
}
