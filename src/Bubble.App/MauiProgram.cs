using Bubble.App.Pages;
using Bubble.App.Services;
using Microsoft.Extensions.Logging;
using ZXing.Net.Maui.Controls;

namespace Bubble.App;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.UseBarcodeReader()
			.ConfigureFonts(fonts =>
			{
				// Laut DESIGN.md: Display = Fraunces, Body = Inter (beide SIL OFL, Lizenzen in docs/licenses)
				fonts.AddFont("Inter-Regular.ttf", "Body");
				fonts.AddFont("Inter-Medium.ttf", "BodyMedium");
				fonts.AddFont("Inter-SemiBold.ttf", "BodySemibold");
				fonts.AddFont("Inter-Bold.ttf", "BodyBold");
				fonts.AddFont("Fraunces-Regular.ttf", "Display");
			});

		// Entry/Editor sitzen in einem eigenen Rahmen (Style „Field“), daher ohne native Linie/Rahmen
		Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping("NoChrome", (handler, _) => RemoveChrome(handler.PlatformView));
		Microsoft.Maui.Handlers.EditorHandler.Mapper.AppendToMapping("NoChrome", (handler, _) => RemoveChrome(handler.PlatformView));

		builder.Services.AddSingleton<AccountService>();
		builder.Services.AddSingleton<FeedService>();
		foreach (var page in typeof(MauiProgram).Assembly.GetTypes().Where(t => t.IsSubclassOf(typeof(ContentPage)) && !t.IsAbstract))
			builder.Services.AddTransient(page);

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
