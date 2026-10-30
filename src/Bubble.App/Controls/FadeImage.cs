namespace Bubble.App.Controls;

/// <summary>
/// Bild, das weich einblendet, sobald es geladen ist. Fotos werden erst nach dem Aufbau der Seite dekodiert;
/// ohne Einblenden sieht man erst einen leeren Rahmen oder einen Farbverlauf und dann ein Aufpoppen (Flackern).
/// Bleibt die Quelle gleich (z. B. beim erneuten Binden derselben Karte), bleibt das Bild einfach stehen.
/// </summary>
public sealed class FadeImage : Image
{
    ImageSource? _shown;

    public FadeImage()
    {
        Opacity = 0;
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Source)) SourceChanged();
            else if (e.PropertyName == nameof(IsLoading) && !IsLoading) Show();
        };
    }

    void SourceChanged()
    {
        if (ReferenceEquals(Source, _shown)) return;
        _shown = Source;

        this.CancelAnimations();
        Opacity = Source is null ? 1 : 0;
        // Notbremse, falls IsLoading nie wechselt (z. B. Bild schon im Speicher)
        if (Source is not null) Dispatcher.DispatchDelayed(TimeSpan.FromSeconds(1.5), Show);
    }

    void Show()
    {
        if (Opacity >= 1) return;
        _ = this.FadeToAsync(1, 200, Easing.CubicOut);
    }
}
