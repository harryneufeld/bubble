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
	/// Zurück-Taste: Seiten schließen sich der Reihe nach, ein anderer Tab führt zurück zu „Heute“.
	/// Auf „Heute“ beendet erst zweimal Zurück innerhalb von zwei Sekunden die App.
	/// </summary>
	protected override bool OnBackButtonPressed() => HandleBack() || base.OnBackButtonPressed();

	/// <summary>True, wenn Zurück verarbeitet wurde; false, wenn die App jetzt beendet werden darf.</summary>
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
			return false; // zweites Zurück: App beenden

		_lastBackOnHome = DateTime.UtcNow;
		ShowHint("Nochmal zurück, um Bubble zu schließen");
		return true;
	}

	static void ShowHint(string text)
	{
#if ANDROID
		Android.Widget.Toast.MakeText(Platform.AppContext, text, Android.Widget.ToastLength.Short)?.Show();
#endif
	}
}
