using Android.App;
using Android.Content.PM;
using Android.OS;
using AndroidX.Activity;

namespace Bubble.App;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        // Otherwise the light default window background sits behind the pages and flashes on page changes.
        Window?.SetBackgroundDrawable(new Android.Graphics.Drawables.ColorDrawable(Android.Graphics.Color.ParseColor("#0B0A10")));
        // Registered last, asked first: close pages, tabs go to "Today", on "Today" press back twice to exit.
        OnBackPressedDispatcher.AddCallback(this, new BackCallback(this));
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
