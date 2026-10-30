using System.Runtime.CompilerServices;
using Microsoft.Maui.Controls.Shapes;

namespace Bubble.App.Controls;

/// <summary>Ring around an avatar: new story (gradient), seen (subtle) or none.</summary>
public enum AvatarRing { None, New, Seen }

/// <summary>
/// Round profile picture. Without an image it shows the initials on a gradient derived from the
/// name – so every person always looks the same. Optionally with a story ring and plus badge.
/// The view is built once and only updated afterwards: in lists it is rebound constantly while
/// scrolling, and rebuilding the child elements every time visibly costs performance.
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
        BindableProperty.Create(name, type, typeof(AvatarView), defaultValue, propertyChanged: (b, _, _) => ((AvatarView)b).Update());

    /// <summary>The small plus on the avatar was tapped ("You" in the story bar: share a highlight).</summary>
    public event EventHandler? PlusTapped;

    // Gradients for initials – all with white text >= 4.5:1. Created once and shared by all avatars.
    static readonly Brush[] InitialBrushes = new[]
    {
        ("#C2185B", "#6D3CF5"), ("#6D3CF5", "#0E7490"), ("#B4235A", "#9A3412"),
        ("#0F766E", "#4338CA"), ("#7C2D92", "#BE185D"), ("#1D4ED8", "#6D28D9"),
    }.Select(p => (Brush)new LinearGradientBrush(
        [new GradientStop(Color.FromArgb(p.Item1), 0), new GradientStop(Color.FromArgb(p.Item2), 1)],
        new Point(0, 0), new Point(1, 1))).ToArray();

    static readonly Brush SeenRingBrush = new SolidColorBrush(Color.FromArgb("#3A3548"));
    static readonly ConditionalWeakTable<byte[], ImageSource> Sources = new();

    static ImageSource SourceFor(byte[] bytes) =>
        Sources.GetValue(bytes, b => ImageSource.FromStream(() => new MemoryStream(b)));

    readonly Border _ring = new() { StrokeShape = new Ellipse(), IsVisible = false, InputTransparent = true };
    readonly Border _circle = new() { StrokeShape = new Ellipse(), StrokeThickness = 0, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
    readonly Image _photo = new FadeImage { Aspect = Aspect.AspectFill, IsVisible = false };
    readonly Grid _initialsBackground = new();
    readonly Label _initials = new() { TextColor = Colors.White, FontFamily = "BodyBold", HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
    readonly Border _plus = new() { StrokeShape = new Ellipse(), StrokeThickness = 3, HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.End, IsVisible = false };
    readonly Image _plusIcon = new() { Source = "ic_plus.png" };
    readonly Grid _root = new();

    public AvatarView()
    {
        var resources = Application.Current!.Resources;
        _initialsBackground.Children.Add(_initials);
        _circle.Content = new Grid { Children = { _initialsBackground, _photo } };

        _plus.Stroke = new SolidColorBrush((Color)resources["Canvas"]);
        _plus.Background = (Brush)resources["CtaGradient"];
        _plus.Content = _plusIcon;
        _plus.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => PlusTapped?.Invoke(this, EventArgs.Empty)) });
        SemanticProperties.SetDescription(_plus, "Share highlight");

        _root.Children.Add(_ring);
        _root.Children.Add(_circle);
        _root.Children.Add(_plus);
        Content = _root;
        Update();
    }

    void Update()
    {
        var size = Size;
        var hasRing = Ring != AvatarRing.None;
        var ring = hasRing ? Math.Max(2, size * 0.045) : 0;
        var inner = size - 2 * (ring + (hasRing ? Math.Max(2, size * 0.045) : 0));

        _root.WidthRequest = _root.HeightRequest = size;

        _ring.IsVisible = hasRing;
        if (hasRing)
        {
            _ring.StrokeThickness = ring;
            _ring.Stroke = Ring == AvatarRing.New ? (Brush)Application.Current!.Resources["BubbleGradient"] : SeenRingBrush;
        }

        _circle.WidthRequest = _circle.HeightRequest = inner;

        if (Image is { Length: > 0 } bytes)
        {
            _photo.Source = SourceFor(bytes);
            _photo.IsVisible = true;
            _initialsBackground.IsVisible = false;
        }
        else
        {
            _photo.Source = null;
            _photo.IsVisible = false;
            _initialsBackground.IsVisible = true;
            _initialsBackground.Background = InitialBrushes[(uint)StableHash(Name) % InitialBrushes.Length];
            _initials.Text = Initials(Name);
            _initials.FontSize = inner * 0.38;
        }

        _plus.IsVisible = ShowPlus;
        if (ShowPlus)
        {
            var badge = size * 0.32;
            _plus.WidthRequest = _plus.HeightRequest = badge;
            _plusIcon.Margin = badge * 0.18;
        }

        SemanticProperties.SetDescription(this, string.IsNullOrEmpty(Name) ? "Profile picture" : $"Profile picture of {Name}");
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

    // string.GetHashCode is randomized per process – we need stable colors here
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
