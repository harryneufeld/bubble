using Bubble.App.Pages;

namespace Bubble.App;

public partial class AppShell : Shell
{
	public AppShell()
	{
		InitializeComponent();
		foreach (var page in new[] { typeof(OnboardingPage), typeof(FeedPage), typeof(ComposePage), typeof(BackupPage),
		                             typeof(InvitePage), typeof(JoinPage), typeof(StoryPage), typeof(ProfileEditPage), typeof(PostPage) })
			Routing.RegisterRoute(page.Name, page);
	}

	DateTime _lastBackOnHome = DateTime.MinValue;

	/// <summary>
	/// Back button: pages close one after another, another tab leads back to "Today".
	/// On "Today" the app only exits after pressing back twice within two seconds.
	/// </summary>
	protected override bool OnBackButtonPressed() => HandleBack() || base.OnBackButtonPressed();

	/// <summary>True if back was handled; false if the app may exit now.</summary>
	public bool HandleBack()
	{
		var location = (CurrentState?.Location?.OriginalString ?? "").Split('?')[0];
		var depth = location.Split('/', StringSplitOptions.RemoveEmptyEntries).Length;
		if (depth > 1)
		{
			_ = GoToAsync("..");
			return true;
		}
		if (!location.StartsWith("//home", StringComparison.Ordinal))
		{
			_ = GoToAsync("//home");
			return true;
		}

		if (DateTime.UtcNow - _lastBackOnHome < TimeSpan.FromSeconds(2))
			return false; // second back: exit the app

		_lastBackOnHome = DateTime.UtcNow;
		ShowHint("Press back again to close Bubble");
		return true;
	}

	static void ShowHint(string text)
	{
#if ANDROID
		Android.Widget.Toast.MakeText(Platform.AppContext, text, Android.Widget.ToastLength.Short)?.Show();
#endif
	}
}
