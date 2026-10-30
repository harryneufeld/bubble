using SkiaSharp;

namespace Bubble.App.Editor;

/// <summary>A font in the editor: shown in the picker under <see cref="Alias"/>, drawn by Skia from <see cref="File"/>.</summary>
public sealed record EditorFont(string Key, string Name, string File, string Alias);

/// <summary>
/// The fonts of the editor. All ship inside the app (DESIGN.md: never from a CDN);
/// the files are also packaged as assets under "editorfonts/" so Skia can load them.
/// </summary>
public static class EditorFonts
{
    public static readonly IReadOnlyList<EditorFont> All =
    [
        new("modern", "Modern", "Inter-Bold.ttf", "BodyBold"),
        new("classic", "Classic", "DMSerifDisplay-Regular.ttf", "DMSerif"),
        new("poster", "POSTER", "BebasNeue-Regular.ttf", "Bebas"),
        new("hand", "Handwritten", "CaveatBrush-Regular.ttf", "CaveatBrush"),
        new("script", "Script", "Pacifico-Regular.ttf", "Pacifico"),
        new("type", "Typewriter", "SpaceMono-Bold.ttf", "SpaceMono"),
        new("soft", "Soft serif", "Fraunces-Regular.ttf", "Display"),
    ];

    public static EditorFont Default => All[0];

    public static EditorFont Get(string key) => All.FirstOrDefault(f => f.Key == key) ?? Default;

    static readonly Dictionary<string, SKTypeface> Loaded = [];
    static Task? _loading;

    /// <summary>Loads all typefaces once. Must have finished before drawing.</summary>
    public static Task LoadAsync() => _loading ??= LoadAllAsync();

    static async Task LoadAllAsync()
    {
        foreach (var font in All)
        {
            await using var stream = await FileSystem.OpenAppPackageFileAsync("editorfonts/" + font.File);
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            Loaded[font.Key] = SKTypeface.FromData(SKData.CreateCopy(buffer.ToArray())) ?? SKTypeface.Default;
        }
    }

    public static SKTypeface Typeface(string key) =>
        Loaded.TryGetValue(key, out var face) ? face : Loaded.GetValueOrDefault(Default.Key) ?? SKTypeface.Default;
}
