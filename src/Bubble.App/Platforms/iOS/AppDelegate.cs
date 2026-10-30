using Bubble.App.Services;
using Foundation;
using UIKit;

namespace Bubble.App;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

	/// <summary>"Open in Bubble" / "Copy to Bubble" for an image from another app: becomes a new highlight.</summary>
	public override bool OpenUrl(UIApplication application, NSUrl url, NSDictionary options)
	{
		if (!url.IsFileUrl) return base.OpenUrl(application, url, options);
		var scoped = url.StartAccessingSecurityScopedResource();
		try
		{
			var data = NSData.FromUrl(url);
			if (data is null || data.Length == 0) return false;
			ShareInbox.Offer(new ShareInbox.Shared(data.ToArray(), null));
			return true;
		}
		finally
		{
			if (scoped) url.StopAccessingSecurityScopedResource();
		}
	}
}
