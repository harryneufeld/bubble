using Bubble.App.Pages;
using Bubble.Core;
using Microsoft.Maui.Controls.Shapes;

namespace Bubble.App.Controls;

/// <summary>
/// The box of memories: a table of prints you can push around in every direction. Only prints in view exist;
/// those that leave are reused for the ones that come in. Panning and tapping both go through one layer on top,
/// because prints moving under the finger would disturb their own gestures.
/// </summary>
public sealed class MemoryTable : ContentView
{
    const double Friction = 0.94, StopBelow = 0.03;

    readonly Grid _surface = new() { IsClippedToBounds = true };
    readonly Dictionary<(int Column, int Row), Print> _active = [];
    readonly Stack<Print> _pool = new();
    readonly IDispatcherTimer _glide;
    IReadOnlyList<PostItem> _memories = [];
    MemoryGrid _grid = new(0);
    double _panX, _panY, _startX, _startY, _lastX, _lastY, _speedX, _speedY;
    DateTime _lastAt;

    public event EventHandler<PostItem>? MemoryTapped;

    public MemoryTable()
    {
        var touch = new BoxView { Color = Colors.Transparent };
        var pan = new PanGestureRecognizer();
        pan.PanUpdated += OnPan;
        touch.GestureRecognizers.Add(pan);
        var tap = new TapGestureRecognizer();
        tap.Tapped += OnTap;
        touch.GestureRecognizers.Add(tap);

        Content = new Grid { IsClippedToBounds = true, Children = { _surface, touch } };
        _glide = Dispatcher.CreateTimer();
        _glide.Interval = TimeSpan.FromMilliseconds(16);
        _glide.Tick += (_, _) => Glide();
        SizeChanged += (_, _) => Render();
    }

    public void SetMemories(IReadOnlyList<PostItem> memories, bool backToStart)
    {
        _memories = memories;
        _grid = new MemoryGrid(memories.Count);
        foreach (var print in _active.Values) Release(print);
        _active.Clear();
        if (backToStart) _panX = _panY = 0;
        Render();
    }

    public void Stop() => _glide.Stop();

    void OnPan(object? sender, PanUpdatedEventArgs e)
    {
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _glide.Stop();
                _startX = _panX;
                _startY = _panY;
                _lastX = _lastY = _speedX = _speedY = 0;
                _lastAt = DateTime.UtcNow;
                break;
            case GestureStatus.Running:
                var ms = Math.Max(1, (DateTime.UtcNow - _lastAt).TotalMilliseconds);
                _speedX = 0.7 * (_lastX - e.TotalX) / ms + 0.3 * _speedX;
                _speedY = 0.7 * (_lastY - e.TotalY) / ms + 0.3 * _speedY;
                _lastX = e.TotalX;
                _lastY = e.TotalY;
                _lastAt = DateTime.UtcNow;
                _panX = _startX - e.TotalX;
                _panY = _startY - e.TotalY;
                Render();
                break;
            case GestureStatus.Completed:
                if (DateTime.UtcNow - _lastAt < TimeSpan.FromMilliseconds(80)) _glide.Start();
                break;
        }
    }

    void Glide()
    {
        _speedX *= Friction;
        _speedY *= Friction;
        if (Math.Abs(_speedX) + Math.Abs(_speedY) < StopBelow)
        {
            _glide.Stop();
            return;
        }
        _panX += _speedX * 16;
        _panY += _speedY * 16;
        Render();
    }

    void OnTap(object? sender, TappedEventArgs e)
    {
        if (e.GetPosition(this) is not { } at) return;
        var hit = _active.Values
            .Where(p => at.X >= p.TranslationX && at.X <= p.TranslationX + MemoryGrid.PrintWidth && at.Y >= p.TranslationY && at.Y <= p.TranslationY + MemoryGrid.PrintHeight)
            .OrderByDescending(p => p.ZIndex)
            .FirstOrDefault();
        if (hit?.Item is { } item) MemoryTapped?.Invoke(this, item);
    }

    void Render()
    {
        if (Width <= 0 || Height <= 0) return;
        (_panX, _panY) = _grid.Clamp(_panX, _panY, Width, Height);

        var visible = _grid.Visible(_panX, _panY, Width, Height).ToList();
        var keep = visible.Select(c => (c.Column, c.Row)).ToHashSet();
        foreach (var key in _active.Keys.Where(k => !keep.Contains(k)).ToList())
        {
            Release(_active[key]);
            _active.Remove(key);
        }
        foreach (var cell in visible)
        {
            var key = (cell.Column, cell.Row);
            if (!_active.TryGetValue(key, out var print))
            {
                print = Take();
                print.Show(_memories[cell.Index]);
                print.Rotation = cell.Rotation;
                print.ZIndex = cell.Layer;
                _active[key] = print;
            }
            print.TranslationX = cell.Left - _panX;
            print.TranslationY = cell.Top - _panY;
        }
    }

    Print Take()
    {
        if (_pool.TryPop(out var print))
        {
            print.IsVisible = true;
            return print;
        }
        print = new Print();
        _surface.Children.Add(print);
        return print;
    }

    void Release(Print print)
    {
        print.IsVisible = false;
        _pool.Push(print);
    }

    /// <summary>A small paper print with the caption on its bottom edge.</summary>
    sealed class Print : Border
    {
        readonly Grid _photo = new() { HeightRequest = 114, IsClippedToBounds = true };
        readonly FadeImage _image = new() { Aspect = Aspect.AspectFill };
        readonly Label _note = new() { FontFamily = "BodyBold", FontSize = 13, TextColor = Colors.White, Margin = 8, MaxLines = 5, LineBreakMode = LineBreakMode.TailTruncation };
        readonly Label _caption = new() { FontFamily = "CaveatBrush", FontSize = 15, Padding = new Thickness(1, 4, 1, 0), LineBreakMode = LineBreakMode.TailTruncation };

        public PostItem? Item { get; private set; }

        public Print()
        {
            var resources = Application.Current!.Resources;
            BackgroundColor = (Color)resources["Paper"];
            Stroke = Color.FromArgb("#40000000");
            StrokeThickness = 1;
            StrokeShape = new RoundRectangle { CornerRadius = 3 };
            Padding = new Thickness(7, 7, 7, 5);
            WidthRequest = MemoryGrid.PrintWidth;
            HorizontalOptions = LayoutOptions.Start;
            VerticalOptions = LayoutOptions.Start;
            InputTransparent = true;
            _caption.TextColor = (Color)resources["PaperInk"];
            _photo.Children.Add(_image);
            _photo.Children.Add(_note);
            Content = new VerticalStackLayout { Children = { _photo, _caption } };
        }

        public void Show(PostItem item)
        {
            if (Item == item) return;
            Item = item;
            _image.IsVisible = item.HasImage;
            _image.Source = null;
            if (item.Source.Post.Message.Image is { Length: > 0 } photo) _ = LoadAsync(item, photo);
            _note.IsVisible = item.IsTextOnly;
            _note.Text = item.IsTextOnly ? item.Text : null;
            _photo.Background = item.TextBackground;
            _caption.Text = item.PolaroidCaption;
        }

        async Task LoadAsync(PostItem item, byte[] photo)
        {
            var thumbnail = await Thumbnails.ForAsync(photo);
            if (Item == item) _image.Source = thumbnail; // the print may already show something else
        }
    }
}
