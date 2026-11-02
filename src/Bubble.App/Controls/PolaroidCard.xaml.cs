using Bubble.App.Pages;

namespace Bubble.App.Controls;

public partial class PolaroidCard : ContentView
{
    public PolaroidCard()
    {
        InitializeComponent();
        Polaroid.EnableZoom();
        Polaroid.DoubleTapped += OnDoubleTapped;
    }

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();
        if (BindingContext is PostItem post) Polaroid.Show(post);
    }

    async void OnLikeClicked(object? sender, EventArgs e)
    {
        if (BindingContext is not PostItem post) return;
        _ = Pop(LikeButton);
        await post.ToggleLikeAsync();
    }

    async void OnDoubleTapped(object? sender, EventArgs e)
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
