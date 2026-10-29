using Microsoft.Maui.Controls.Shapes;

namespace Bubble.App.Controls;

/// <summary>Ring um ein Avatar: neue Story (Verlauf), angesehen (dezent) oder keiner.</summary>
public enum AvatarRing { None, New, Seen }

/// <summary>
/// Rundes Profilbild. Ohne Bild zeigt es die Initialen auf einem Verlauf, der sich aus dem
/// Namen ergibt – so sieht jede Person immer gleich aus. Optional mit Story-Ring und Plus-Badge.
/// </summary>
public sealed class AvatarView : ContentView
{
    public static readonly BindableProperty ImageProperty = Create(nameof(Image), typeof(byte[]));
    public static readonly BindableProperty NameProperty = Create(nameof(Name), typeof(string), "");
    public static readonly BindableProperty SizeProperty = Create(nameof(Size), typeof(double), 40.0);
    public static readonly BindableProperty RingProperty = Create(nameof(Ring), typeof(AvatarRing), AvatarRing.None);
    public static readonly BindableProperty ShowPlusProperty = Create(nameof(ShowPlus), typeof(bool), false);

    public byte[]? Image { get => (byte[]?)GetValue(ImageProperty); set => SetValue(ImageProperty, value); }
    public string Name { get => (string)GetValue(NameProperty); set => SetValue(NameProperty, value); }
    public double Size { get => (double)GetValue(SizeProperty); set => SetValue(SizeProperty, value); }
    public AvatarRing Ring { get => (AvatarRing)GetValue(RingProperty); set => SetValue(RingProperty, value); }
    public bool ShowPlus { get => (bool)GetValue(ShowPlusProperty); set => SetValue(ShowPlusProperty, value); }

    static BindableProperty Create(string name, Type type, object? defaultValue = null) =>
        BindableProperty.Create(name, type, typeof(AvatarView), defaultValue, propertyChanged: (b, _, _) => ((AvatarView)b).Rebuild());

    // Verläufe für Initialen – alle mit weißem Text >= 4,5:1
    static readonly (string, string)[] Palettes =
    [
        ("#C2185B", "#6D3CF5"), ("#6D3CF5", "#0E7490"), ("#B4235A", "#9A3412"),
        ("#0F766E", "#4338CA"), ("#7C2D92", "#BE185D"), ("#1D4ED8", "#6D28D9"),
    ];

    public AvatarView() => Rebuild();

    void Rebuild()
    {
        var size = Size;
        var ring = Ring == AvatarRing.None ? 0 : Math.Max(2, size * 0.045);
        var gap = Ring == AvatarRing.None ? 0 : Math.Max(2, size * 0.045);
        var inner = size - 2 * (ring + gap);

        View face;
        if (Image is { Length: > 0 } bytes)
        {
            face = new Image { Source = ImageSource.FromStream(() => new MemoryStream(bytes)), Aspect = Aspect.AspectFill };
        }
        else
        {
            var (from, to) = Palettes[(uint)StableHash(Name) % Palettes.Length];
            face = new Grid
            {
                Background = new LinearGradientBrush([new GradientStop(Color.FromArgb(from), 0), new GradientStop(Color.FromArgb(to), 1)], new Point(0, 0), new Point(1, 1)),
                Children =
                {
                    new Label
                    {
                        Text = Initials(Name), TextColor = Colors.White, FontFamily = "BodyBold", FontSize = inner * 0.38,
                        HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center,
                    },
                },
            };
        }

        var circle = new Border
        {
            StrokeShape = new Ellipse(), StrokeThickness = 0, WidthRequest = inner, HeightRequest = inner,
            HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center, Content = face,
        };

        var root = new Grid { WidthRequest = size, HeightRequest = size };
        if (Ring != AvatarRing.None)
        {
            root.Children.Add(new Border
            {
                StrokeShape = new Ellipse(), StrokeThickness = ring,
                Stroke = Ring == AvatarRing.New ? (Brush)Application.Current!.Resources["BubbleGradient"] : new SolidColorBrush(Color.FromArgb("#3A3548")),
            });
        }
        root.Children.Add(circle);

        if (ShowPlus)
        {
            var badge = size * 0.32;
            root.Children.Add(new Border
            {
                StrokeShape = new Ellipse(), WidthRequest = badge, HeightRequest = badge,
                Stroke = (Color)Application.Current!.Resources["Canvas"], StrokeThickness = 3,
                Background = (Brush)Application.Current.Resources["CtaGradient"],
                HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.End,
                Content = new Image { Source = "ic_plus.png", Margin = badge * 0.18 },
            });
        }

        SemanticProperties.SetDescription(this, string.IsNullOrEmpty(Name) ? "Profilbild" : $"Profilbild von {Name}");
        Content = root;
    }

    public static string Initials(string? name)
    {
        var parts = (name ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => "?",
            1 => parts[0][..1].ToUpperInvariant(),
            _ => (parts[0][..1] + parts[^1][..1]).ToUpperInvariant(),
        };
    }

    // string.GetHashCode ist pro Prozess zufällig – hier brauchen wir stabile Farben
    static int StableHash(string? s)
    {
        unchecked
        {
            var h = 23;
            foreach (var c in s ?? "") h = h * 31 + c;
            return h;
        }
    }
}
