using Bubble.App.Pages;
using Bubble.App.Services;
using Bubble.Core;
using Microsoft.Maui.Controls.Shapes;

namespace Bubble.App.Controls;

/// <summary>
/// Today's bubbles, floating. A bubble with more new highlights is bigger. If they don't fit the width,
/// the cluster scrolls sideways.
/// </summary>
public sealed class BubbleCluster : ContentView
{
    const double LabelRoom = 22, Inset = 8;

    readonly AbsoluteLayout _stage = new();
    readonly List<(View View, double Phase, double Speed)> _floating = [];
    readonly IDispatcherTimer _timer;
    readonly DateTime _start = DateTime.UtcNow;
    IReadOnlyList<StoryRing> _rings = [];
    double _height = 240, _scale = 1, _contentWidth;

    public event EventHandler<StoryRing>? RingTapped;
    public event EventHandler? PlusTapped;

    public BubbleCluster()
    {
        Content = new ScrollView { Orientation = ScrollOrientation.Horizontal, HorizontalScrollBarVisibility = ScrollBarVisibility.Never, Content = _stage };
        _timer = Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(33);
        _timer.Tick += (_, _) => Float();
        SizeChanged += (_, _) => Center();
    }

    /// <summary>Classic has room for big bubbles, the box keeps them smaller so the pile gets the screen.</summary>
    public void SetSize(double height, double scale)
    {
        if (height == _height && scale == _scale) return;
        _height = height;
        _scale = scale;
        Build();
    }

    public void SetRings(IReadOnlyList<StoryRing> rings)
    {
        _rings = rings;
        Build();
    }

    public void Start()
    {
        if (!Motion.IsReduced) _timer.Start();
    }

    public void Stop() => _timer.Stop();

    void Build()
    {
        _stage.Children.Clear();
        _floating.Clear();
        HeightRequest = _stage.HeightRequest = _height;

        var sizes = _rings.Select(r => r.Size * _scale + LabelRoom).ToList();
        var spots = BubblePacking.Place(sizes, _height - 2 * Inset);
        _contentWidth = 0;
        for (var i = 0; i < _rings.Count; i++)
        {
            var view = BubbleFor(_rings[i], _rings[i].Size * _scale, sizes[i]);
            AbsoluteLayout.SetLayoutBounds(view, new Rect(spots[i].X + Inset, spots[i].Y + Inset, sizes[i], sizes[i]));
            _stage.Children.Add(view);
            _floating.Add((view, i * 1.7, 2.4 + i % 3 * 0.5));
            _contentWidth = Math.Max(_contentWidth, spots[i].X + sizes[i] + 2 * Inset);
        }
        Center();
    }

    void Center()
    {
        _stage.WidthRequest = Math.Max(_contentWidth, Width);
        _stage.TranslationX = Width > _contentWidth ? (Width - _contentWidth) / 2 : 0;
        _stage.Margin = 0;
    }

    View BubbleFor(StoryRing ring, double size, double box)
    {
        var avatar = new AvatarView { Size = size, Image = ring.Image, Name = ring.Name, Ring = ring.Ring, ShowPlus = ring.ShowPlus, HorizontalOptions = LayoutOptions.Center };
        avatar.PlusTapped += (_, _) => PlusTapped?.Invoke(this, EventArgs.Empty);

        // The shine of a soap bubble
        var inner = size * 0.82;
        var sheen = new Ellipse
        {
            WidthRequest = inner,
            HeightRequest = inner,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            InputTransparent = true,
            Fill = new RadialGradientBrush(
                [new GradientStop(Color.FromArgb("#8CFFFFFF"), 0), new GradientStop(Color.FromArgb("#00FFFFFF"), 0.3f)],
                new Point(0.3, 0.24), 0.9),
        };
        var label = new Label
        {
            Text = ring.Title,
            FontSize = 12,
            FontFamily = "BodyMedium",
            TextColor = ring.TitleColor,
            HorizontalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.TailTruncation,
            InputTransparent = true,
        };
        var layout = new VerticalStackLayout
        {
            Spacing = 4,
            WidthRequest = box,
            Children = { new Grid { WidthRequest = size, HeightRequest = size, HorizontalOptions = LayoutOptions.Center, Children = { avatar, sheen } }, label },
        };
        SemanticProperties.SetDescription(layout, ring.Description);
        layout.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => RingTapped?.Invoke(this, ring)) });
        return layout;
    }

    void Float()
    {
        var t = (DateTime.UtcNow - _start).TotalSeconds;
        foreach (var (view, phase, speed) in _floating)
        {
            view.TranslationX = 3 * Math.Sin(t / speed + phase);
            view.TranslationY = 5 * Math.Sin(t / speed * 1.3 + phase * 2);
        }
    }
}
