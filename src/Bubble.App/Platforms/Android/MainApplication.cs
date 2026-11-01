using Android.App;
using Android.Runtime;

namespace Bubble.App;

[Application]
public class MainApplication : MauiApplication
{
	public MainApplication(IntPtr handle, JniHandleOwnership ownership)
		: base(handle, ownership)
	{
	}

	public override void OnCreate()
	{
		base.OnCreate();
#if BUBBLE_FCM
		// Also runs when only the push service is started, e.g. by a wake while the app is closed.
		try { FcmPushProvider.EnsureInitialized(); }
		catch (Exception e) { Android.Util.Log.Warn("Bubble", $"Firebase not initialised: {e.Message}"); }
#endif
	}

	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
