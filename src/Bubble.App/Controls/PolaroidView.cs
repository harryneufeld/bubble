using System.Runtime.CompilerServices;
using Bubble.App.Pages;
using Microsoft.Maui.Controls.Shapes;
using SkiaSharp;

namespace Bubble.App.Controls;

/// <summary>
/// A highlight in a paper frame that follows the photo's proportions, so nothing is cut off.
/// As a polaroid (opened highlights): slightly tilted, with handwriting on the wide bottom edge, and the photo can be zoomed.
/// With <see cref="Flat"/> (stories): straight, a thin frame, as big as the screen allows.
/// </summary>
public sealed class PolaroidView : ContentView
{
    const double PolaroidPaper = 14, FlatPaper = 5, CaptionRoom = 96, MaxZoom = 4;
    static readonly ConditionalWeakTable<byte[], object> Ratios = new();

    readonly Border _frame;
    readonly Grid _photo = new() { IsClippedToBounds = true };
    readonly FadeImage _image = new() { Aspect = Aspect.AspectFill, AnchorX = 0, AnchorY = 0 };
    readonly Label _note = new() { FontFamily = "BodyBold", FontSize = 26, LineHeight = 1.15, TextColor = Colors.White, HorizontalTextAlignment = TextAlignment.Center, VerticalOptions = LayoutOptions.Center, Margin = 20 };
    readonly Label _caption = new() { FontFamily = "CaveatBrush", FontSize = 25, LineHeight = 1.1, Padding = new Thickness(4, 12, 4, 14), MinimumHeightRequest = 72, VerticalTextAlignment = TextAlignment.Center };
    readonly PanGestureRecognizer _zoomedPan = new();
    double _ratio = 1, _zoom = 1, _pinchStartZoom = 1, _offsetX, _offsetY;
    bool _isNote, _flat;

    /// <summary>Double tap on the photo. Needs <see cref="EnableZoom"/>.</summary>
    public event EventHandler? DoubleTapped;

    /// <summary>Straight, with a thin frame and no handwriting, filling the height it is given.</summary>
    public bool Flat
    {
        get => _flat;
        set
        {
            _flat = value;
            _frame.Rotation = value ? 0 : -1.5;
            _frame.Padding = value ? FlatPaper : new Thickness(PolaroidPaper, PolaroidPaper, PolaroidPaper, 0);
            _caption.IsVisible = !value;
            Fit();
        }
    }

    public double MaxPhotoHeight { get; set; } = 520;

    public PolaroidView()
    {
        var resources = Application.Current!.Resources;
        _caption.TextColor = (Color)resources["PaperInk"];
        _photo.Children.Add(_image);
        _photo.Children.Add(_note);
        _frame = new Border
        {
            BackgroundColor = (Color)resources["Paper"],
            StrokeShape = new RoundRectangle { CornerRadius = 4 },
            Padding = new Thickness(PolaroidPaper, PolaroidPaper, PolaroidPaper, 0),
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Rotation = -1.5,
            Content = new VerticalStackLayout { Children = { _photo, _caption } },
        };
        Content = _frame;
        SizeChanged += (_, _) => Fit();
        _zoomedPan.PanUpdated += OnZoomedPan;
    }

    public void Show(PostItem item)
    {
        _isNote = item.IsTextOnly;
        _ratio = item.Source.Post.Message.Image is { Length: > 0 } bytes ? RatioOf(bytes) : 1;
        ResetZoom();
        _image.IsVisible = !_isNote;
        _image.Source = item.Image;
        SemanticProperties.SetDescription(_image, item.ImageDescription);
        _note.IsVisible = _isNote;
        _note.Text = _isNote ? item.Text : null;
        _photo.Background = _isNote ? item.TextBackground : (Brush)new SolidColorBrush((Color)Application.Current!.Resources["SurfaceRaised"]);
        _caption.Text = item.PolaroidCaption;
        Fit();
    }

    void Fit()
    {
        if (Width <= 0) return;
        var paper = _flat ? FlatPaper : PolaroidPaper;
        var width = Math.Min(Width - (_flat ? 0 : 16), _flat ? double.MaxValue : 460) - 2 * paper;
        var limit = !_flat ? MaxPhotoHeight : Height > 0 ? Height - 2 * paper : MaxPhotoHeight;
        var height = width / _ratio;
        if (height > limit)
        {
            height = limit;
            width = height * _ratio;
        }

        _frame.WidthRequest = width + 2 * paper;
        // A long text may grow past the square, and in a story it fills the screen; a photo is exactly as tall as its proportions say
        _photo.HeightRequest = !_isNote ? height : _flat ? limit : -1;
        _photo.MinimumHeightRequest = _isNote && !_flat ? Math.Min(width, limit) : 0;
    }

    /// <summary>Two fingers zoom the photo inside its frame, one finger moves it while zoomed.</summary>
    public void EnableZoom()
    {
        var pinch = new PinchGestureRecognizer();
        pinch.PinchUpdated += OnPinch;
        _photo.GestureRecognizers.Add(pinch);
        var doubleTap = new TapGestureRecognizer { NumberOfTapsRequired = 2 };
        doubleTap.Tapped += (_, _) => DoubleTapped?.Invoke(this, EventArgs.Empty);
        _photo.GestureRecognizers.Add(doubleTap);
#if ANDROID
        _photo.HandlerChanged += (_, _) =>
        {
            if (_photo.Handler?.PlatformView is not Android.Views.View view) return;
            // Otherwise the page starts scrolling as soon as the fingers move and takes the gesture away
            view.Touch += (_, e) =>
            {
                if (e.Event is { PointerCount: > 1 } || _zoom > 1) view.Parent?.RequestDisallowInterceptTouchEvent(true);
                e.Handled = false;
            };
        };
#endif
    }

    void OnPinch(object? sender, PinchGestureUpdatedEventArgs e)
    {
        if (_isNote) return;
        switch (e.Status)
        {
            case GestureStatus.Started:
                _pinchStartZoom = _zoom;
                break;
            case GestureStatus.Running:
                _zoom = Math.Clamp(_zoom + (e.Scale - 1) * _pinchStartZoom, 1, MaxZoom);
                // Keep the spot between the fingers where it is
                _image.Scale = _zoom;
                Move(_offsetX - e.ScaleOrigin.X * _image.Width * (_zoom - _pinchStartZoom),
                     _offsetY - e.ScaleOrigin.Y * _image.Height * (_zoom - _pinchStartZoom));
                break;
            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                if (_zoom <= 1.02) ResetZoom();
                else
                {
                    _offsetX = _image.TranslationX;
                    _offsetY = _image.TranslationY;
                    if (!_photo.GestureRecognizers.Contains(_zoomedPan)) _photo.GestureRecognizers.Add(_zoomedPan);
                }
                break;
        }
    }

    void OnZoomedPan(object? sender, PanUpdatedEventArgs e)
    {
        if (e.StatusType == GestureStatus.Running) Move(_offsetX + e.TotalX, _offsetY + e.TotalY);
        else if (e.StatusType is GestureStatus.Completed or GestureStatus.Canceled)
        {
            _offsetX = _image.TranslationX;
            _offsetY = _image.TranslationY;
        }
    }

    /// <summary>Never past the edges of the photo.</summary>
    void Move(double x, double y)
    {
        _image.TranslationX = Math.Clamp(x, -_image.Width * (_zoom - 1), 0);
        _image.TranslationY = Math.Clamp(y, -_image.Height * (_zoom - 1), 0);
    }

    void ResetZoom()
    {
        _zoom = 1;
        _offsetX = _offsetY = 0;
        _image.Scale = 1;
        _image.TranslationX = _image.TranslationY = 0;
        _photo.GestureRecognizers.Remove(_zoomedPan);
    }

    /// <summary>Width by height, read from the file header without decoding the photo.</summary>
    static double RatioOf(byte[] bytes) => (double)Ratios.GetValue(bytes, b =>
    {
        using var codec = SKCodec.Create(new MemoryStream(b));
        return codec is { Info.Height: > 0 } ? (double)codec.Info.Width / codec.Info.Height : 1d;
    });
}
