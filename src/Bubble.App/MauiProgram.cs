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
				// Laut DESIGN.md: Display = Fraunces, Body = Inter (beide SIL OFL, Lizenzen in docs/licenses)
				fonts.AddFont("Inter-Regular.ttf", "Body");
				fonts.AddFont("Inter-Medium.ttf", "BodyMedium");
				fonts.AddFont("Inter-SemiBold.ttf", "BodySemibold");
				fonts.AddFont("Fraunces-Regular.ttf", "Display");
			});

		// Entry/Editor sitzen in einem eigenen Rahmen (Style „Field“), daher ohne native Linie/Rahmen
		Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping("NoChrome", (handler, _) => RemoveChrome(handler.PlatformView));
		Microsoft.Maui.Handlers.EditorHandler.Mapper.AppendToMapping("NoChrome", (handler, _) => RemoveChrome(handler.PlatformView));

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

	static void RemoveChrome(object view)
	{
#if ANDROID
		if (view is Android.Widget.EditText edit)
			edit.BackgroundTintList = Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.Transparent);
#elif IOS || MACCATALYST
		if (view is UIKit.UITextField field)
			field.BorderStyle = UIKit.UITextBorderStyle.None;
#elif WINDOWS
		if (view is Microsoft.UI.Xaml.Controls.TextBox box)
		{
			var clear = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
			box.BorderThickness = new Microsoft.UI.Xaml.Thickness(0);
			box.Resources["TextControlBorderThemeThicknessFocused"] = new Microsoft.UI.Xaml.Thickness(0);
			box.Resources["TextControlBackgroundPointerOver"] = clear;
			box.Resources["TextControlBackgroundFocused"] = clear;
		}
#endif
	}
}
