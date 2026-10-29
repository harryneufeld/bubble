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
        var options = new List<string>();
        if (MediaPicker.Default.IsCaptureSupported) options.Add(TakePhoto);
        options.Add(FromGallery);
        string? choice = options.Count == 1 && !offerRemove
            ? FromGallery
            : await page.DisplayActionSheetAsync(title, "Abbrechen", offerRemove ? Remove : null, [.. options]);

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
