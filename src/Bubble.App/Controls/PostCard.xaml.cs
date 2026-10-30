using Bubble.App.Pages;

namespace Bubble.App.Controls;

public partial class PostCard : ContentView
{
    /// <summary>Double-tapping the image gives a heart. Only on the post page, where tapping opens nothing else.</summary>
    public static readonly BindableProperty DoubleTapToLikeProperty =
        BindableProperty.Create(nameof(DoubleTapToLike), typeof(bool), typeof(PostCard), false, propertyChanged: (b, _, n) => ((PostCard)b).EnableDoubleTap((bool)n));

    public bool DoubleTapToLike { get => (bool)GetValue(DoubleTapToLikeProperty); set => SetValue(DoubleTapToLikeProperty, value); }

    public PostCard()
    {
        InitializeComponent();
        PhotoImage.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Image.IsLoading) && !PhotoImage.IsLoading) ShowPhoto();
        };
    }

    ImageSource? _shown;

    // The image is only decoded after the page is built. Without a fade you would see an empty frame and then a
    // pop-in (flicker); this fades it in softly. If the card already shows the same image, it stays put.
    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();
        var source = PhotoImage.Source;
        if (ReferenceEquals(source, _shown)) return;

        _shown = source;
        PhotoImage.CancelAnimations();
        PhotoImage.Opacity = source is null ? 1 : 0;
        // Notbremse, falls IsLoading nie wechselt (z. B. Bild schon im Speicher)
        if (source is not null) Dispatcher.DispatchDelayed(TimeSpan.FromSeconds(1.5), ShowPhoto);
    }

    void ShowPhoto()
    {
        if (PhotoImage.Opacity >= 1) return;
        _ = PhotoImage.FadeToAsync(1, 220, Easing.CubicOut);
    }

    void EnableDoubleTap(bool on)
    {
        if (!on) return;
        var tap = new TapGestureRecognizer { NumberOfTapsRequired = 2 };
        tap.Tapped += OnDoubleTapped;
        Media.GestureRecognizers.Add(tap);
    }

    async void OnLikeClicked(object? sender, EventArgs e)
    {
        if (BindingContext is not PostItem post) return;
        _ = Pop(LikeButton);
        await post.ToggleLikeAsync();
    }

    async void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (BindingContext is not PostItem post) return;
        if (!post.IsLiked)
        {
            _ = Pop(LikeButton);
            _ = post.ToggleLikeAsync();
        }
        BigHeart.Scale = 0.4;
        await Task.WhenAll(BigHeart.FadeToAsync(1, 120), BigHeart.ScaleToAsync(1.1, 220, Easing.SpringOut));
        await Task.Delay(250);
        await Task.WhenAll(BigHeart.FadeToAsync(0, 220), BigHeart.ScaleToAsync(1.4, 220, Easing.CubicIn));
    }

    static async Task Pop(VisualElement view)
    {
        await view.ScaleToAsync(1.3, 90, Easing.CubicOut);
        await view.ScaleToAsync(1, 140, Easing.SpringOut);
    }
}
