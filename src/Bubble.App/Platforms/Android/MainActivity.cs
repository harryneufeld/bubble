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
        // Hinter den Seiten liegt sonst der helle Standard-Fensterhintergrund und blitzt beim Seitenwechsel auf.
        Window?.SetBackgroundDrawable(new Android.Graphics.Drawables.ColorDrawable(Android.Graphics.Color.ParseColor("#0B0A10")));
        // Zuletzt registriert, zuerst gefragt: Seiten schließen, Tabs zu „Heute“, auf „Heute“ zweimal Zurück zum Beenden.
        OnBackPressedDispatcher.AddCallback(this, new BackCallback(this));
    }

    sealed class BackCallback(MainActivity activity) : OnBackPressedCallback(true)
    {
        public override void HandleOnBackPressed()
        {
            // Sheets, Stories und Onboarding sind Modal-Seiten und kümmern sich selbst: an MAUI weiterreichen.
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
