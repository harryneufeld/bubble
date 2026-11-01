using Bubble.App.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Bubble.App;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();
		UserAppTheme = AppTheme.Dark; // Bubble is deliberately dark (DESIGN.md)
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var window = new Window(new AppShell());
		// Back from the background (also by tapping a notification): show what arrived in the meantime without a manual refresh.
		window.Resumed += (_, _) => _ = IPlatformApplication.Current?.Services.GetService<FeedService>()?.RefreshOnResumeAsync();
		// Brings the push subscription up to date (renews the handle, follows token changes) once the app starts.
		IPlatformApplication.Current?.Services.GetService<PushService>()?.Start();
		return window;
	}
}