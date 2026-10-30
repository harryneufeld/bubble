using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Content;
using AndroidX.Activity;
using Bubble.App.Services;

namespace Bubble.App;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
[IntentFilter([Intent.ActionSend], Categories = [Intent.CategoryDefault], DataMimeType = "image/*")]
[IntentFilter([Intent.ActionSend], Categories = [Intent.CategoryDefault], DataMimeType = "text/plain")]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        // Otherwise the light default window background sits behind the pages and flashes on page changes.
        Window?.SetBackgroundDrawable(new Android.Graphics.Drawables.ColorDrawable(Android.Graphics.Color.ParseColor("#0B0A10")));
        // Registered last, asked first: close pages, tabs go to "Today", on "Today" press back twice to exit.
        OnBackPressedDispatcher.AddCallback(this, new BackCallback(this));
        HandleShare(Intent);
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        HandleShare(intent);
    }

    /// <summary>"Share" from another app: hand the image or text to the compose page.</summary>
    void HandleShare(Intent? intent)
    {
        if (intent?.Action != Intent.ActionSend) return;
        intent.SetAction(Intent.ActionMain); // a rotation or re-creation must not post it twice
        byte[]? image = null;
        var uri = OperatingSystem.IsAndroidVersionAtLeast(33)
            ? intent.GetParcelableExtra(Intent.ExtraStream, Java.Lang.Class.FromType(typeof(Android.Net.Uri))) as Android.Net.Uri
            : intent.GetParcelableExtra(Intent.ExtraStream) as Android.Net.Uri;
        if (uri is not null && intent.Type?.StartsWith("image/") == true)
        {
            try
            {
                using var input = ContentResolver?.OpenInputStream(uri);
                if (input is not null)
                {
                    using var buffer = new MemoryStream();
                    input.CopyTo(buffer);
                    image = buffer.ToArray();
                }
            }
            catch (Exception ex) when (ex is Java.IO.IOException or System.IO.IOException or Java.Lang.SecurityException) { }
        }
        var text = intent.GetStringExtra(Intent.ExtraText);
        if (image is null && string.IsNullOrWhiteSpace(text)) return;
        ShareInbox.Offer(new ShareInbox.Shared(image, text));
    }

    sealed class BackCallback(MainActivity activity) : OnBackPressedCallback(true)
    {
        public override void HandleOnBackPressed()
        {
            // Sheets, stories and onboarding are modal pages and handle it themselves: pass it on to MAUI.
            if (Shell.Current is not AppShell shell || shell.Navigation.ModalStack.Count > 0)
            {
                PassOn();
                return;
            }
            if (!shell.HandleBack()) activity.Finish();
        }

        void PassOn()
        {
            Enabled = false;
            activity.OnBackPressedDispatcher.OnBackPressed();
            Enabled = true;
        }
    }
}
