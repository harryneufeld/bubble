using Bubble.App.Pages;

namespace Bubble.App;

public partial class AppShell : Shell
{
	public AppShell()
	{
		InitializeComponent();
		foreach (var page in new[] { typeof(OnboardingPage), typeof(FeedPage), typeof(ComposePage), typeof(BackupPage),
		                             typeof(InvitePage), typeof(JoinPage), typeof(StoryPage), typeof(ProfileEditPage) })
			Routing.RegisterRoute(page.Name, page);
	}
}
