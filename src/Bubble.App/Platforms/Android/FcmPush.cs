#if BUBBLE_FCM
using System.Reflection;
using Android.App;
using Android.Gms.Common;
using Android.Gms.Extensions;
using Bubble.App.Services;
using Bubble.Core;
using Firebase;
using Firebase.Messaging;

namespace Bubble.App;

/// <summary>
/// FCM without google-services.json: the Firebase settings come from push.props at build time, so a fork only has to
/// fill in its own project.
/// </summary>
sealed class FcmPushProvider : IPushProvider
{
    public string Platform => "fcm";

    public static bool IsAvailable =>
        GoogleApiAvailability.Instance.IsGooglePlayServicesAvailable(Android.App.Application.Context) == ConnectionResult.Success;

    /// <summary>Safe to call more than once. Has to run before Firebase is used, in any process start (activity or push service).</summary>
    public static void EnsureInitialized()
    {
        var context = Android.App.Application.Context;
        if (FirebaseApp.GetApps(context).Count > 0) return;
        FirebaseApp.InitializeApp(context, new FirebaseOptions.Builder()
            .SetProjectId(Setting("Bubble.Fcm.ProjectId"))!
            .SetApplicationId(Setting("Bubble.Fcm.AppId"))!
            .SetApiKey(Setting("Bubble.Fcm.ApiKey"))!
            .SetGcmSenderId(Setting("Bubble.Fcm.SenderId"))!
            .Build());
    }

    public async Task<string?> GetTokenAsync(CancellationToken ct = default)
    {
        try
        {
            EnsureInitialized();
            var token = await FirebaseMessaging.Instance.GetToken().AsAsync<Java.Lang.String>();
            return token?.ToString();
        }
        catch (Exception e) when (e is Java.Lang.Exception or InvalidOperationException or Java.Util.Concurrent.ExecutionException)
        {
            return null; // no network, or Firebase refused the settings
        }
    }

    static string Setting(string key) =>
        typeof(FcmPushProvider).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().First(a => a.Key == key).Value ?? "";
}

/// <summary>Receives the (content-free) wake and the token changes from Firebase.</summary>
[Service(Exported = false)]
[IntentFilter(["com.google.firebase.MESSAGING_EVENT"])]
public class BubbleFirebaseService : FirebaseMessagingService
{
    public override void OnNewToken(string token)
    {
        base.OnNewToken(token);
        Push()?.SyncAsync();
    }

    public override void OnMessageReceived(RemoteMessage message)
    {
        base.OnMessageReceived(message);
        // The system gives a high-priority message about 20 seconds. This thread belongs to Firebase, so waiting here is fine.
        if (Push() is { } push)
            Task.Run(push.HandleWakeAsync).Wait(TimeSpan.FromSeconds(18));
    }

    static PushService? Push() => IPlatformApplication.Current?.Services.GetService<PushService>();
}
#endif
