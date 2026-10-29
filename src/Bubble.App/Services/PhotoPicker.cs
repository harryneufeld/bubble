using Bubble.App.Controls;

namespace Bubble.App.Services;

/// <summary>Foto aufnehmen oder auswählen – mit Kamera, wo es eine gibt.</summary>
public static class PhotoPicker
{
    const string TakePhoto = "Foto aufnehmen";
    const string FromGallery = "Aus Galerie wählen";
    public const string Remove = "Entfernen";

    /// <summary>Fragt nach der Quelle. Null bei Abbruch oder „Entfernen“ (dann wird <paramref name="removed"/> aufgerufen).</summary>
    public static async Task<FileResult?> PickAsync(Page page, string title, bool offerRemove = false, Action? removed = null)
    {
        var options = new List<SheetOption>();
        if (MediaPicker.Default.IsCaptureSupported) options.Add(new(TakePhoto, "Mit der Kamera", "📷"));
        options.Add(new(FromGallery, "Ein vorhandenes Foto", "🖼️"));
        if (offerRemove) options.Add(new(Remove, null, "🗑️", Destructive: true));

        string? choice;
        if (options.Count == 1) choice = FromGallery;
        else if (await SheetPage.ChooseAsync(page, title, "Standort- und Kameradaten werden entfernt.", options) is { } picked) choice = options[picked].Title;
        else choice = null;

        try
        {
            switch (choice)
            {
                case TakePhoto:
                    return await MediaPicker.Default.CapturePhotoAsync();
                case FromGallery:
                    return (await MediaPicker.Default.PickPhotosAsync(new MediaPickerOptions { SelectionLimit = 1 })).FirstOrDefault();
                case Remove:
                    removed?.Invoke();
                    return null;
                default:
                    return null;
            }
        }
        catch (PermissionException)
        {
            await page.DisplayAlertAsync("Kein Zugriff", "Erlaube Bubble in den Einstellungen den Zugriff auf Kamera bzw. Fotos.", "OK");
            return null;
        }
    }
}
