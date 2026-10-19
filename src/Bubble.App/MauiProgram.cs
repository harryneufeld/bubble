using Bubble.App.Pages;
using Bubble.App.Services;
using Microsoft.Extensions.Logging;

namespace Bubble.App;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

		builder.Services.AddSingleton<AccountService>();
		builder.Services.AddTransient<OnboardingPage>();
		builder.Services.AddTransient<BubblesPage>();
		builder.Services.AddTransient<FeedPage>();
		builder.Services.AddTransient<ComposePage>();
		builder.Services.AddTransient<BackupPage>();

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
