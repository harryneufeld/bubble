using Bubble.App.Controls;

namespace Bubble.App.Services;

/// <summary>Take or choose a photo – with the camera where there is one.</summary>
public static class PhotoPicker
{
    const string TakePhoto = "Take photo";
    const string FromGallery = "Choose from gallery";
    public const string Remove = "Remove";

    /// <summary>Asks for the source. Null on cancel or "Remove" (then <paramref name="removed"/> is called).</summary>
    public static async Task<FileResult?> PickAsync(Page page, string title, bool offerRemove = false, Action? removed = null)
    {
        var options = new List<SheetOption>();
        if (MediaPicker.Default.IsCaptureSupported) options.Add(new(TakePhoto, "With the camera", "📷"));
        options.Add(new(FromGallery, "An existing photo", "🖼️"));
        if (offerRemove) options.Add(new(Remove, null, "🗑️", Destructive: true));

        string? choice;
        if (options.Count == 1) choice = FromGallery;
        else if (await SheetPage.ChooseAsync(page, title, "Location and camera data are removed.", options) is { } picked) choice = options[picked].Title;
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
            await page.DisplayAlertAsync("No access", "Allow Bubble to access the camera or photos in the settings.", "OK");
            return null;
        }
    }
}
