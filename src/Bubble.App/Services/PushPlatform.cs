using Bubble.Core;

namespace Bubble.App.Services;

/// <summary>What push needs from the operating system. Only Android has an implementation so far (Platforms/Android).</summary>
public static partial class PushPlatform
{
    /// <summary>Whether this build has push at all.</summary>
    public static partial bool IsSupported();

    /// <summary>The token source for this platform, or null if push is not possible right now.</summary>
    public static partial IPushProvider? CreateProvider();

    /// <summary>Asks for the notification permission where the system requires it. True if notifications may be shown.</summary>
    public static partial Task<bool> RequestNotificationPermissionAsync();

    /// <summary>Shows a notification. A newer one with the same <paramref name="tag"/> replaces the older one.</summary>
    public static partial void ShowNotification(string tag, string title, string text);

#if !ANDROID
    public static partial bool IsSupported() => false;

    public static partial IPushProvider? CreateProvider() => null;

    public static partial Task<bool> RequestNotificationPermissionAsync() => Task.FromResult(false);

    public static partial void ShowNotification(string tag, string title, string text) { }
#endif
}
