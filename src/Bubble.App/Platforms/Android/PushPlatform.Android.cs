using Android.App;
using Android.Content;
using AndroidX.Core.App;
using Bubble.Core;

namespace Bubble.App.Services;

public static partial class PushPlatform
{
    const string ChannelId = "highlights";

    public static partial bool IsSupported() =>
#if BUBBLE_FCM
        true;
#else
        false;
#endif

    public static partial IPushProvider? CreateProvider() =>
#if BUBBLE_FCM
        FcmPushProvider.IsAvailable ? new FcmPushProvider() : null;
#else
        null;
#endif

    public static partial async Task<bool> RequestNotificationPermissionAsync()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(33)) return true;
        var status = await Permissions.CheckStatusAsync<Permissions.PostNotifications>();
        if (status != PermissionStatus.Granted)
            status = await MainThread.InvokeOnMainThreadAsync(Permissions.RequestAsync<Permissions.PostNotifications>);
        return status == PermissionStatus.Granted;
    }

    public static partial void ShowNotification(string tag, string title, string text)
    {
        var context = Android.App.Application.Context;
        EnsureChannel(context);

        var icon = context.Resources?.GetIdentifier("ic_bubbles", "drawable", context.PackageName) is > 0 and var id
            ? id
            : Android.Resource.Drawable.IcDialogInfo;
        var open = PendingIntent.GetActivity(
            context, 0,
            new Intent(context, typeof(MainActivity)).SetFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop),
            PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);

        // On the lock screen only a generic line shows; the real text appears once the phone is unlocked.
        var generic = new NotificationCompat.Builder(context, ChannelId)
            .SetSmallIcon(icon)!
            .SetContentTitle("Bubble")!
            .SetContentText("New in your bubbles")!
            .Build();
        var notification = new NotificationCompat.Builder(context, ChannelId)
            .SetSmallIcon(icon)!
            .SetContentTitle(title)!
            .SetContentText(text)!
            .SetAutoCancel(true)!
            .SetContentIntent(open)!
            .SetCategory(NotificationCompat.CategoryMessage)!
            .SetVisibility(NotificationCompat.VisibilityPrivate)!
            .SetPublicVersion(generic)!
            .Build();

        NotificationManagerCompat.From(context).Notify(tag, 0, notification);
    }

    static void EnsureChannel(Context context)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26)) return;
        if (context.GetSystemService(Context.NotificationService) is not NotificationManager manager) return;
        if (manager.GetNotificationChannel(ChannelId) is not null) return;
        manager.CreateNotificationChannel(new NotificationChannel(ChannelId, "New highlights", NotificationImportance.Default)
        {
            Description = "When someone shares a highlight in one of your bubbles",
        });
    }
}
