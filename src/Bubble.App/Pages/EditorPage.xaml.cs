using Bubble.App.Controls;
using Bubble.App.Editor;
using Bubble.App.Services;
using Microsoft.Maui.Controls.Shapes;
using SkiaSharp;
using SkiaSharp.Views.Maui;

namespace Bubble.App.Pages;

/// <summary>
/// Editor like Canva: photos, texts and shapes on a 9:16 or 4:5 canvas. Drag to move, pinch to scale and rotate
/// (mouse wheel scales on desktop). The finished design is rendered into one JPEG.
/// </summary>
public partial class EditorPage : ContentPage
{
    enum Panel { None, Font, Color, Style, Background }
    enum Grip { None, Left, Right, Top, Bottom }

    const int MaxHistory = 60;

    readonly TaskCompletionSource<byte[]?> _result = new();
    readonly List<Design> _undo = [];
    readonly List<Design> _redo = [];
    Design _design = new();
    Guid? _selectedId;
    Panel _panel;
    bool _closing;

    // Gestures (canvas units)
    readonly Dictionary<long, SKPoint> _touches = [];
    readonly Dictionary<long, SKPoint> _gestureTouches = [];
    float _viewScale = 1;
    (float X, float Y, float Scale, float Rotation) _gestureLayer;
    bool _moved, _wasSelected, _onEmpty, _guideV, _guideH;
    DateTime _lastWheel;

    // Crop mode: gestures move and zoom the photo inside its frame, edges resize the frame
    bool _crop;
    Grip _grip;
    (float Zoom, float CropX, float CropY, float Width, float Height) _gestureCrop;

    // Text overlay
    TextLayer? _editingText;
    bool _editingNew;

    EditorPage(byte[]? startPhoto)
    {
        InitializeComponent();
        if (startPhoto is not null)
        {
            _design.Background = BackgroundKind.Photo;
            _design.BackgroundPhoto = startPhoto;
            AddPhotoLayer(startPhoto, select: false);
        }
        BuildFormatToggle();
        RefreshTools();
        UpdateHistoryButtons();
    }

    /// <summary>Opens the editor; returns the JPEG or null if closed without finishing.</summary>
    public static async Task<byte[]?> OpenAsync(Page host, byte[]? startPhoto = null)
    {
        await EditorFonts.LoadAsync();
        var page = new EditorPage(startPhoto);
        await host.Navigation.PushModalAsync(page);
        return await page._result.Task;
    }

    Layer? Selected => _selectedId is { } id ? _design.Layers.FirstOrDefault(l => l.Id == id) : null;

    ImageLayer? CropLayer => _crop && Selected is ImageLayer { Image: not null } image ? image : null;

    void Redraw() => CanvasView.InvalidateSurface();

    // ---------- History ----------

    /// <summary>Call before every change, so the change can be undone.</summary>
    void Checkpoint()
    {
        _undo.Add(_design.Clone());
        if (_undo.Count > MaxHistory) _undo.RemoveAt(0);
        _redo.Clear();
        UpdateHistoryButtons();
    }

    void OnUndo(object? sender, EventArgs e) => Step(_undo, _redo);
    void OnRedo(object? sender, EventArgs e) => Step(_redo, _undo);

    void Step(List<Design> from, List<Design> to)
    {
        if (from.Count == 0) return;
        var formatBefore = _design.Format;
        to.Add(_design.Clone());
        _design = from[^1];
        from.RemoveAt(from.Count - 1);
        if (Selected is null) _selectedId = null;
        if (CropLayer is null) _crop = false;
        if (_design.Format != formatBefore) { BuildFormatToggle(); LayoutCanvas(); }
        UpdateHistoryButtons();
        RefreshTools();
        Redraw();
    }

    void UpdateHistoryButtons()
    {
        UndoButton.Opacity = _undo.Count > 0 ? 1 : 0.4;
        RedoButton.Opacity = _redo.Count > 0 ? 1 : 0.4;
    }

    // ---------- Canvas ----------

    void OnStageSizeChanged(object? sender, EventArgs e) => LayoutCanvas();

    /// <summary>Largest size with the canvas aspect ratio that fits the stage.</summary>
    void LayoutCanvas()
    {
        var w = Stage.Width - Stage.Padding.HorizontalThickness;
        var h = Stage.Height - Stage.Padding.VerticalThickness;
        if (w <= 0 || h <= 0) return;
        var aspect = Design.Width / _design.Height;
        if (w / h > aspect) w = h * aspect; else h = w / aspect;
        CanvasFrame.WidthRequest = w;
        CanvasFrame.HeightRequest = h;
        Redraw();
    }

    void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Black);
        _viewScale = e.Info.Width / Design.Width;
        canvas.Scale(_viewScale);
        DesignRenderer.Draw(canvas, _design);
        var unit = 1 / _viewScale;
        DesignRenderer.DrawGuides(canvas, _design, _guideV, _guideH, unit);
        if (CropLayer is { } crop) DesignRenderer.DrawCropOverlay(canvas, crop, unit);
        else if (Selected is { } selected) DesignRenderer.DrawSelection(canvas, selected, unit);
    }

    void OnTouch(object? sender, SKTouchEventArgs e)
    {
        e.Handled = true;
        var point = new SKPoint(e.Location.X / _viewScale, e.Location.Y / _viewScale);
        switch (e.ActionType)
        {
            case SKTouchAction.Pressed:
                _touches[e.Id] = point;
                if (_touches.Count == 1 && CropLayer is { } crop)
                {
                    _grip = GripAt(crop, point);
                    _onEmpty = false;
                    _moved = false;
                }
                else if (_touches.Count == 1)
                {
                    // On empty space the selection stays for a pinch with two fingers; a tap clears it
                    var hit = DesignRenderer.HitTest(_design, point, 24 / _viewScale);
                    _wasSelected = hit is not null && hit.Id == _selectedId;
                    _onEmpty = hit is null;
                    _moved = false;
                    if (hit is not null && !_wasSelected) Select(hit);
                }
                BeginGesture();
                Redraw();
                break;

            case SKTouchAction.Moved when _touches.ContainsKey(e.Id):
                _touches[e.Id] = point;
                MoveGesture();
                break;

            case SKTouchAction.Released or SKTouchAction.Cancelled when _touches.Remove(e.Id):
                if (_touches.Count > 0) { BeginGesture(); break; }
                _guideV = _guideH = false;
                if (_moved && _crop) RefreshOptions(); // the aspect ratio may have changed
                if (!_moved && e.ActionType == SKTouchAction.Released) OnTap();
                Redraw();
                break;

            case SKTouchAction.WheelChanged when Selected is { } layer:
                if (DateTime.UtcNow - _lastWheel > TimeSpan.FromMilliseconds(600)) Checkpoint();
                _lastWheel = DateTime.UtcNow;
                if (CropLayer is { } cropped)
                {
                    cropped.CropZoom *= 1 + e.WheelDelta / 1200f;
                    DesignRenderer.ClampCrop(cropped);
                }
                else layer.Scale = Math.Clamp(layer.Scale * (1 + e.WheelDelta / 1200f), 0.05f, 20);
                Redraw();
                break;
        }
    }

    /// <summary>Remembers where fingers and layer were; moves are relative to this.</summary>
    void BeginGesture()
    {
        _gestureTouches.Clear();
        foreach (var (id, p) in _touches.Take(2)) _gestureTouches[id] = p;
        if (Selected is { } layer) _gestureLayer = (layer.X, layer.Y, layer.Scale, layer.Rotation);
        if (Selected is ImageLayer image) _gestureCrop = (image.CropZoom, image.CropX, image.CropY, image.FrameWidth, image.FrameHeight);
    }

    void MoveGesture()
    {
        if (Selected is not { } layer || _gestureTouches.Count == 0) return;
        var ids = _gestureTouches.Keys.ToList();
        if (!ids.All(_touches.ContainsKey)) { BeginGesture(); return; }

        if (ids.Count == 1 && _onEmpty) return;

        var start0 = _gestureTouches[ids[0]];
        var now0 = _touches[ids[0]];
        if (!_moved)
        {
            // Small jitter is still a tap
            if (ids.Count == 1 && SKPoint.Distance(start0, now0) < 8 / _viewScale) return;
            Checkpoint();
            _moved = true;
        }

        if (CropLayer is { } crop)
        {
            MoveCrop(crop, ids, start0, now0);
            return;
        }

        var (x, y, scale, rotation) = _gestureLayer;
        if (ids.Count == 1)
        {
            layer.X = x + now0.X - start0.X;
            layer.Y = y + now0.Y - start0.Y;
        }
        else
        {
            var start1 = _gestureTouches[ids[1]];
            var now1 = _touches[ids[1]];
            var v0 = start1 - start0;
            var v1 = now1 - now0;
            if (v0.Length > 1)
                layer.Scale = Math.Clamp(scale * v1.Length / v0.Length, 0.05f, 20);
            var angle = rotation + (float)((Math.Atan2(v1.Y, v1.X) - Math.Atan2(v0.Y, v0.X)) * 180 / Math.PI);
            var snapped = MathF.Round(angle / 90) * 90;
            layer.Rotation = Math.Abs(angle - snapped) < 4 ? snapped : angle;
            layer.X = x + (now0.X + now1.X - start0.X - start1.X) / 2;
            layer.Y = y + (now0.Y + now1.Y - start0.Y - start1.Y) / 2;
        }

        // Snap to the center lines
        var snap = 10 / _viewScale;
        _guideV = Math.Abs(layer.X - Design.Width / 2) < snap;
        _guideH = Math.Abs(layer.Y - _design.Height / 2) < snap;
        if (_guideV) layer.X = Design.Width / 2;
        if (_guideH) layer.Y = _design.Height / 2;
        Redraw();
    }

    /// <summary>Which frame edge is under the point; edges can be grabbed along their whole length.</summary>
    Grip GripAt(ImageLayer layer, SKPoint point)
    {
        var local = DesignRenderer.ToLocal(layer, point);
        var slop = 28 / _viewScale / layer.Scale;
        float halfW = layer.FrameWidth / 2, halfH = layer.FrameHeight / 2;
        var alongX = Math.Abs(local.X) <= halfW + slop;
        var alongY = Math.Abs(local.Y) <= halfH + slop;
        if (alongY && Math.Abs(local.X + halfW) < slop) return Grip.Left;
        if (alongY && Math.Abs(local.X - halfW) < slop) return Grip.Right;
        if (alongX && Math.Abs(local.Y + halfH) < slop) return Grip.Top;
        if (alongX && Math.Abs(local.Y - halfH) < slop) return Grip.Bottom;
        return Grip.None;
    }

    /// <summary>A canvas movement in the layer's own axes (rotation and scale undone).</summary>
    static SKPoint LocalDelta(Layer layer, SKPoint delta)
    {
        var rad = -layer.Rotation * Math.PI / 180;
        var (sin, cos) = ((float)Math.Sin(rad), (float)Math.Cos(rad));
        return new SKPoint((delta.X * cos - delta.Y * sin) / layer.Scale, (delta.X * sin + delta.Y * cos) / layer.Scale);
    }

    void MoveCrop(ImageLayer layer, List<long> ids, SKPoint start0, SKPoint now0)
    {
        var (zoom, cropX, cropY, width, height) = _gestureCrop;
        var image = DesignRenderer.ImageSize(layer.Image!);
        // Frame units per photo pixel at gesture start, so the photo stays put while the frame changes
        var startScale = Math.Max(width / image.Width, height / image.Height) * zoom;
        var (x, y, _, _) = _gestureLayer;

        if (ids.Count == 2)
        {
            var start1 = _gestureTouches[ids[1]];
            var now1 = _touches[ids[1]];
            var ratio = (now1 - now0).Length / Math.Max(1, (start1 - start0).Length);
            var pan = LocalDelta(layer, new SKPoint((now0.X + now1.X - start0.X - start1.X) / 2, (now0.Y + now1.Y - start0.Y - start1.Y) / 2));
            layer.CropZoom = zoom * ratio;
            var scale = startScale * ratio;
            layer.CropX = cropX - pan.X / scale / image.Width;
            layer.CropY = cropY - pan.Y / scale / image.Height;
        }
        else if (_grip == Grip.None)
        {
            var pan = LocalDelta(layer, now0 - start0);
            layer.CropX = cropX - pan.X / startScale / image.Width;
            layer.CropY = cropY - pan.Y / startScale / image.Height;
        }
        else
        {
            const float min = 80;
            var d = LocalDelta(layer, now0 - start0);
            float newW = width, newH = height, shiftX = 0, shiftY = 0;
            switch (_grip)
            {
                case Grip.Right: newW = Math.Max(min, width + d.X); shiftX = (newW - width) / 2; break;
                case Grip.Left: newW = Math.Max(min, width - d.X); shiftX = -(newW - width) / 2; break;
                case Grip.Bottom: newH = Math.Max(min, height + d.Y); shiftY = (newH - height) / 2; break;
                case Grip.Top: newH = Math.Max(min, height - d.Y); shiftY = -(newH - height) / 2; break;
            }
            layer.FrameWidth = newW;
            layer.FrameHeight = newH;
            // The frame grows on one side only: move its center there, and the visible part of the photo with it
            var rad = layer.Rotation * Math.PI / 180;
            var (sin, cos) = ((float)Math.Sin(rad), (float)Math.Cos(rad));
            layer.X = x + (shiftX * cos - shiftY * sin) * layer.Scale;
            layer.Y = y + (shiftX * sin + shiftY * cos) * layer.Scale;
            layer.CropX = cropX + shiftX / startScale / image.Width;
            layer.CropY = cropY + shiftY / startScale / image.Height;
            layer.CropZoom = startScale / Math.Max(newW / image.Width, newH / image.Height);
        }
        DesignRenderer.ClampCrop(layer);
        Redraw();
    }

    void SetCrop(bool on)
    {
        _crop = on;
        _panel = Panel.None;
        RefreshTools();
        Redraw();
    }

    /// <summary>New frame shape with about the same area; the crop stays centered where it was.</summary>
    void SetAspect(ImageLayer layer, float ratio) => Change(() =>
    {
        var area = layer.FrameWidth * layer.FrameHeight;
        layer.FrameWidth = MathF.Sqrt(area * ratio);
        layer.FrameHeight = layer.FrameWidth / ratio;
        DesignRenderer.ClampCrop(layer);
    });

    async void OnTap()
    {
        if (_crop) return;
        if (_onEmpty)
        {
            if (Selected is not null) Select(null);
            return;
        }
        switch (Selected)
        {
            case ImageLayer { Image: null } placeholder:
                await FillPhotoAsync(placeholder);
                break;
            case TextLayer text when _wasSelected:
                EditText(text, isNew: false);
                break;
        }
    }

    // ---------- Tools ----------

    void RefreshTools()
    {
        Tools.Children.Clear();
        if (CropLayer is { } crop)
        {
            Tool("✅", "Done", () => SetCrop(false));
            Tool("↺", "Reset", () => Change(crop.ResetCrop));
            RefreshOptions();
            return;
        }
        _crop = false;
        if (Selected is not null) Tool("✖️", "Deselect", () => Select(null));
        switch (Selected)
        {
            case null:
                Tool("🖼️", "Photo", () => _ = AddPhotoAsync());
                Tool("🔤", "Text", () => EditText(null, isNew: true));
                Tool("🔷", "Shape", () => _ = AddShapeAsync());
                Tool("🎨", "Background", () => TogglePanel(Panel.Background), _panel == Panel.Background);
                Tool("🧩", "Templates", () => _ = ChooseTemplateAsync());
                break;
            case TextLayer text:
                Tool("✏️", "Edit", () => EditText(text, isNew: false));
                Tool("Aa", "Font", () => TogglePanel(Panel.Font), _panel == Panel.Font);
                Tool("🎨", "Color", () => TogglePanel(Panel.Color), _panel == Panel.Color);
                Tool("✨", "Style", () => TogglePanel(Panel.Style), _panel == Panel.Style);
                Tool(text.Align switch { SKTextAlign.Left => "⇤", SKTextAlign.Right => "⇥", _ => "↔" }, "Align", () => Change(() =>
                    text.Align = text.Align switch { SKTextAlign.Center => SKTextAlign.Left, SKTextAlign.Left => SKTextAlign.Right, _ => SKTextAlign.Center }));
                CommonTools(text);
                break;
            case ShapeLayer shape:
                Tool("🎨", "Color", () => TogglePanel(Panel.Color), _panel == Panel.Color);
                CommonTools(shape);
                break;
            case ImageLayer image:
                if (image.Image is not null) Tool("✂️", "Crop", () => SetCrop(true));
                Tool("🔁", "Replace", () => _ = FillPhotoAsync(image));
                Tool(image.CornerRadius switch { 0 => "◻️", >= 9999 => "⚪", _ => "▢" }, "Corners", () => Change(() =>
                    image.CornerRadius = image.CornerRadius switch { 0 => 40, >= 9999 => 0, _ => 9999 }));
                CommonTools(image);
                break;
        }
        RefreshOptions();
    }

    void CommonTools(Layer layer)
    {
        Tool("⬆️", "Forward", () => Reorder(layer, +1));
        Tool("⬇️", "Back", () => Reorder(layer, -1));
        Tool("🌗", $"{Math.Round(layer.Opacity * 100)} %", () => Change(() => layer.Opacity = layer.Opacity <= 0.25f ? 1 : layer.Opacity - 0.25f));
        Tool("⧉", "Duplicate", () =>
        {
            Checkpoint();
            var copy = layer.Duplicate();
            _design.Layers.Insert(_design.Layers.IndexOf(layer) + 1, copy);
            Select(copy);
        });
        Tool("🗑️", "Delete", () =>
        {
            Checkpoint();
            _design.Layers.Remove(layer);
            Select(null);
        });
    }

    void Tool(string icon, string label, Action action, bool active = false)
    {
        var tile = new Border
        {
            StrokeShape = new RoundRectangle { CornerRadius = 16 },
            StrokeThickness = active ? 2 : 0,
            Stroke = active ? (Brush)Res("BubbleGradient") : null,
            BackgroundColor = (Color)Res(active ? "Surface" : "SurfaceRaised"),
            WidthRequest = 72, Padding = new Thickness(4, 8),
            Content = new VerticalStackLayout
            {
                Spacing = 3,
                Children =
                {
                    new Label { Text = icon, FontSize = 22, HorizontalOptions = LayoutOptions.Center, TextColor = (Color)Res("Ink") },
                    new Label { Text = label, FontSize = 11, FontFamily = "BodyMedium", HorizontalOptions = LayoutOptions.Center, TextColor = (Color)Res("InkSoft") },
                },
            },
        };
        SemanticProperties.SetDescription(tile, label);
        tile.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(action) });
        Tools.Children.Add(tile);
    }

    static object Res(string key) => Application.Current!.Resources[key];

    void TogglePanel(Panel panel)
    {
        _panel = _panel == panel ? Panel.None : panel;
        RefreshTools();
    }

    /// <summary>A change from the toolbar: undoable, then redraw.</summary>
    void Change(Action change)
    {
        Checkpoint();
        change();
        RefreshTools();
        Redraw();
    }

    void Select(Layer? layer)
    {
        _selectedId = layer?.Id;
        _crop = false;
        _panel = Panel.None;
        RefreshTools();
        Redraw();
    }

    void Reorder(Layer layer, int direction)
    {
        var index = _design.Layers.IndexOf(layer);
        var target = Math.Clamp(index + direction, 0, _design.Layers.Count - 1);
        if (target == index) return;
        Change(() =>
        {
            _design.Layers.RemoveAt(index);
            _design.Layers.Insert(target, layer);
        });
    }

    // ---------- Option rows ----------

    void RefreshOptions()
    {
        Options.Children.Clear();
        var selected = Selected;
        if (CropLayer is { } crop)
        {
            var size = DesignRenderer.ImageSize(crop.Image!);
            var current = crop.FrameWidth / crop.FrameHeight;
            foreach (var (name, ratio) in new[] { ("Original", (float)size.Width / size.Height), ("1:1", 1f), ("4:5", 0.8f), ("3:4", 0.75f), ("16:9", 16 / 9f), ("9:16", 9 / 16f) })
                Chip(name, "BodySemibold", Math.Abs(current - ratio) < 0.01f, () => SetAspect(crop, ratio));
            OptionsScroller.IsVisible = true;
            return;
        }
        switch (_panel)
        {
            case Panel.Font when selected is TextLayer text:
                foreach (var font in EditorFonts.All)
                    Chip(font.Name, font.Alias, text.Font == font.Key, () => Change(() => text.Font = font.Key));
                break;
            case Panel.Style when selected is TextLayer text:
                foreach (var (style, name) in new[] { (TextStyle.Plain, "Plain"), (TextStyle.Shadow, "Shadow"), (TextStyle.Box, "Box") })
                    Chip(name, "BodySemibold", text.Style == style, () => Change(() => text.Style = style));
                break;
            case Panel.Color when selected is TextLayer text:
                foreach (var color in DesignRenderer.Swatches)
                    Swatch(new SolidColorBrush(ToMaui(color)), text.Color == color, () => Change(() => text.Color = color));
                break;
            case Panel.Color when selected is ShapeLayer shape:
                Swatch(GradientBrush(0), shape.Gradient, () => Change(() => shape.Gradient = true));
                foreach (var color in DesignRenderer.Swatches)
                    Swatch(new SolidColorBrush(ToMaui(color)), !shape.Gradient && shape.Color == color, () => Change(() => { shape.Gradient = false; shape.Color = color; }));
                break;
            case Panel.Background:
                Chip("📷 Photo", "BodySemibold", _design.Background == BackgroundKind.Photo, () => _ = PickBackgroundPhotoAsync());
                for (var i = 0; i < DesignRenderer.Gradients.Length; i++)
                {
                    var index = i;
                    Swatch(GradientBrush(i), _design.Background == BackgroundKind.Gradient && _design.GradientIndex == i,
                        () => Change(() => { _design.Background = BackgroundKind.Gradient; _design.GradientIndex = index; }));
                }
                foreach (var color in DesignRenderer.Swatches)
                    Swatch(new SolidColorBrush(ToMaui(color)), _design.Background == BackgroundKind.Solid && _design.BackgroundColor == color,
                        () => Change(() => { _design.Background = BackgroundKind.Solid; _design.BackgroundColor = color; }));
                break;
        }
        OptionsScroller.IsVisible = Options.Children.Count > 0;
    }

    static Color ToMaui(SKColor c) => Color.FromRgba(c.Red, c.Green, c.Blue, c.Alpha);

    static LinearGradientBrush GradientBrush(int index)
    {
        var colors = DesignRenderer.Gradients[index];
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        for (var i = 0; i < colors.Length; i++)
            brush.GradientStops.Add(new GradientStop(ToMaui(colors[i]), i / (float)(colors.Length - 1)));
        return brush;
    }

    void Chip(string text, string fontFamily, bool on, Action action)
    {
        var chip = new Border
        {
            StrokeShape = new RoundRectangle { CornerRadius = 999 },
            StrokeThickness = on ? 2 : 0,
            Stroke = on ? (Brush)Res("BubbleGradient") : null,
            BackgroundColor = (Color)Res("Surface"),
            Padding = new Thickness(14, 6), HeightRequest = 40,
            Content = new Label { Text = text, FontFamily = fontFamily, FontSize = 17, VerticalOptions = LayoutOptions.Center, TextColor = (Color)Res("Ink") },
        };
        SemanticProperties.SetDescription(chip, text);
        SemanticProperties.SetHint(chip, on ? "selected" : "");
        chip.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(action) });
        Options.Children.Add(chip);
    }

    void Swatch(Brush fill, bool on, Action action)
    {
        var swatch = new Border
        {
            StrokeShape = new Ellipse(), WidthRequest = 40, HeightRequest = 40,
            Background = fill, StrokeThickness = on ? 3 : 1,
            Stroke = on ? Colors.White : (Color)Res("Hairline"),
        };
        SemanticProperties.SetDescription(swatch, "Color");
        SemanticProperties.SetHint(swatch, on ? "selected" : "");
        swatch.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(action) });
        Options.Children.Add(swatch);
    }

    // ---------- Format ----------

    void BuildFormatToggle()
    {
        FormatToggle.Children.Clear();
        foreach (var (format, name) in new[] { (DesignFormat.Story, "9:16"), (DesignFormat.Portrait, "4:5") })
        {
            var on = _design.Format == format;
            var chip = new Border
            {
                StrokeShape = new RoundRectangle { CornerRadius = 999 }, StrokeThickness = 0, Padding = new Thickness(12, 7),
                BackgroundColor = (Color)Res(on ? "SurfaceRaised" : "Canvas"),
                Content = new Label { Text = name, FontFamily = "BodySemibold", FontSize = 13, TextColor = (Color)Res(on ? "Ink" : "Muted") },
            };
            SemanticProperties.SetDescription(chip, $"Format {name}");
            SemanticProperties.SetHint(chip, on ? "selected" : "");
            chip.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => SetFormat(format)) });
            FormatToggle.Children.Add(chip);
        }
    }

    /// <summary>Changes the format; layers keep their place relative to the middle.</summary>
    void SetFormat(DesignFormat format)
    {
        if (format == _design.Format) return;
        Checkpoint();
        var shift = (Design.HeightOf(format) - _design.Height) / 2;
        foreach (var layer in _design.Layers) layer.Y += shift;
        _design.Format = format;
        BuildFormatToggle();
        LayoutCanvas();
    }

    // ---------- Adding things ----------

    async Task<byte[]?> PickPhotoAsync(string title)
    {
        var photo = await PhotoPicker.PickAsync(this, title);
        if (photo is null) return null;
        try
        {
            await using var stream = await photo.OpenReadAsync();
            return await ImageSanitizer.SanitizeAsync(stream);
        }
        catch (InvalidDataException)
        {
            await this.ShowAlertAsync("Photo not readable", "Unfortunately this image format is not supported.", "OK");
            return null;
        }
    }

    async Task AddPhotoAsync()
    {
        if (await PickPhotoAsync("Add photo") is not { } bytes) return;
        Checkpoint();
        AddPhotoLayer(bytes, select: true);
    }

    /// <summary>Adds a photo in its own aspect ratio, as large as fits nicely.</summary>
    void AddPhotoLayer(byte[] bytes, bool select)
    {
        var info = SKBitmap.DecodeBounds(bytes);
        var fit = Math.Min(860f / info.Width, _design.Height * 0.6f / info.Height);
        var layer = new ImageLayer
        {
            Image = bytes, FrameWidth = info.Width * fit, FrameHeight = info.Height * fit, CornerRadius = 24,
            X = Design.Width / 2, Y = _design.Height / 2,
        };
        _design.Layers.Add(layer);
        if (select) Select(layer); else Redraw();
    }

    async Task FillPhotoAsync(ImageLayer layer)
    {
        if (await PickPhotoAsync(layer.Image is null ? "Add photo" : "Replace photo") is not { } bytes) return;
        Change(() =>
        {
            layer.Image = bytes;
            layer.ResetCrop();
        });
    }

    async Task PickBackgroundPhotoAsync()
    {
        if (await PickPhotoAsync("Background photo") is not { } bytes) return;
        Change(() => { _design.Background = BackgroundKind.Photo; _design.BackgroundPhoto = bytes; });
    }

    static readonly (ShapeKind Kind, string Name, string Icon)[] ShapeOptions =
    [
        (ShapeKind.Blob, "Bubble", "🫧"), (ShapeKind.Circle, "Circle", "⚪"), (ShapeKind.Rectangle, "Rectangle", "▭"),
        (ShapeKind.Heart, "Heart", "🤍"), (ShapeKind.Star, "Star", "⭐"), (ShapeKind.Line, "Line", "➖"), (ShapeKind.Arrow, "Arrow", "➡️"),
    ];

    async Task AddShapeAsync()
    {
        var options = ShapeOptions.Select(s => new SheetOption(s.Name, null, s.Icon)).ToList();
        if (await SheetPage.ChooseAsync(this, "Add shape", null, options) is not { } index) return;
        var kind = ShapeOptions[index].Kind;
        var shape = new ShapeLayer
        {
            Kind = kind, X = Design.Width / 2, Y = _design.Height / 2,
            Gradient = kind is ShapeKind.Blob or ShapeKind.Circle,
            Width = kind switch { ShapeKind.Rectangle or ShapeKind.Line or ShapeKind.Arrow => 600, _ => 460 },
            Height = kind switch { ShapeKind.Rectangle => 400, ShapeKind.Line or ShapeKind.Arrow => 140, _ => 460 },
            Color = kind is ShapeKind.Line or ShapeKind.Arrow ? SKColors.White : new SKColor(0xFF, 0x4F, 0x9A),
        };
        Checkpoint();
        _design.Layers.Add(shape);
        Select(shape);
    }

    async Task ChooseTemplateAsync()
    {
        var options = Templates.All.Select(t => new SheetOption(t.Name, t.Description, t.Icon)).ToList();
        var message = _design.Layers.Count > 0 ? "Replaces the current design. Undo brings it back." : null;
        if (await SheetPage.ChooseAsync(this, "Templates", message, options) is not { } index) return;
        Checkpoint();
        _design = Templates.All[index].Create(_design.Format);
        Select(null);
    }

    // ---------- Text ----------

    void EditText(TextLayer? layer, bool isNew)
    {
        _editingNew = isNew;
        _editingText = layer ?? new TextLayer { X = Design.Width / 2, Y = _design.Height / 2 };
        var font = EditorFonts.Get(_editingText.Font);
        TextInput.FontFamily = font.Alias;
        TextInput.Text = isNew ? "" : _editingText.Text;
        TextOverlay.IsVisible = true;
        Dispatcher.Dispatch(() => TextInput.Focus());
    }

    void OnTextDone(object? sender, EventArgs e)
    {
        TextOverlay.IsVisible = false;
        TextInput.Unfocus();
        if (_editingText is not { } layer) return;
        _editingText = null;
        var text = (TextInput.Text ?? "").Trim('\n', '\r', ' ');

        if (_editingNew)
        {
            if (text.Length == 0) return;
            Checkpoint();
            layer.Text = text;
            _design.Layers.Add(layer);
            Select(layer);
            return;
        }
        if (text == layer.Text) return;
        Checkpoint();
        if (text.Length == 0) { _design.Layers.Remove(layer); Select(null); return; }
        layer.Text = text;
        Redraw();
    }

    // ---------- Finish ----------

    async void OnDone(object? sender, EventArgs e)
    {
        if (_design.Layers.Any(l => l is ImageLayer { Image: null }) &&
            !await this.ShowAlertAsync("Empty photo frames", "Frames without a photo are left out. Finish anyway?", "Finish", "Keep editing"))
            return;

        DoneButton.IsEnabled = false;
        Busy.IsRunning = true;
        try
        {
            var design = _design.Clone();
            var jpeg = await Task.Run(() => DesignRenderer.Export(design));
            await CloseAsync(jpeg);
        }
        finally
        {
            Busy.IsRunning = false;
            DoneButton.IsEnabled = true;
        }
    }

    async void OnClose(object? sender, EventArgs e) => await ConfirmCloseAsync();

    protected override bool OnBackButtonPressed()
    {
        if (TextOverlay.IsVisible) { OnTextDone(null, EventArgs.Empty); return true; }
        _ = ConfirmCloseAsync();
        return true;
    }

    async Task ConfirmCloseAsync()
    {
        if (_undo.Count > 0)
        {
            var choice = await SheetPage.ChooseAsync(this, "Discard design?", "Your changes will be lost.",
                [new SheetOption("Discard", null, "🗑️", Destructive: true)], "Keep editing");
            if (choice is null) return;
        }
        await CloseAsync(null);
    }

    async Task CloseAsync(byte[]? result)
    {
        if (_closing) return;
        _closing = true;
        await Navigation.PopModalAsync();
        _result.TrySetResult(result);
    }
}
