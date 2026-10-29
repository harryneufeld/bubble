using Bubble.App.Pages;

namespace Bubble.App.Controls;

public partial class PostCard : ContentView
{
    /// <summary>Doppeltippen auf das Bild gibt ein Herz. Nur auf der Post-Seite, wo Tippen nichts anderes öffnet.</summary>
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

    // Das Bild wird erst nach dem Aufbau der Seite dekodiert. Ohne Einblenden sieht man erst einen leeren Rahmen und dann
    // ein Aufpoppen (Flackern); so blendet es weich ein.
    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();
        PhotoImage.CancelAnimations();
        PhotoImage.Opacity = 0;
        // Bleibt die Quelle gleich, wird nichts neu geladen und IsLoading ändert sich nie
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(60), () => { if (!PhotoImage.IsLoading) ShowPhoto(); });
        Dispatcher.DispatchDelayed(TimeSpan.FromSeconds(3), ShowPhoto);
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
