using Bubble.App.Pages;

namespace Bubble.App;

public partial class AppShell : Shell
{
	public AppShell()
	{
		InitializeComponent();
		Routing.RegisterRoute(nameof(OnboardingPage), typeof(OnboardingPage));
		Routing.RegisterRoute(nameof(FeedPage), typeof(FeedPage));
		Routing.RegisterRoute(nameof(ComposePage), typeof(ComposePage));
		Routing.RegisterRoute(nameof(BackupPage), typeof(BackupPage));
	}
}
