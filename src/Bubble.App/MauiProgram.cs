using Bubble.App.Pages;
using Bubble.App.Services;
using Microsoft.Extensions.Logging;
using SkiaSharp.Views.Maui.Controls.Hosting;
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
			.UseSkiaSharp()
			.ConfigureFonts(fonts =>
			{
				// Laut DESIGN.md: Display = Fraunces, Body = Inter (beide SIL OFL, Lizenzen in docs/licenses)
				fonts.AddFont("Inter-Regular.ttf", "Body");
				fonts.AddFont("Inter-Medium.ttf", "BodyMedium");
				fonts.AddFont("Inter-SemiBold.ttf", "BodySemibold");
				fonts.AddFont("Inter-Bold.ttf", "BodyBold");
				fonts.AddFont("Fraunces-Regular.ttf", "Display");
				// Editor fonts (SIL OFL, licenses in docs/licenses)
				fonts.AddFont("BebasNeue-Regular.ttf", "Bebas");
				fonts.AddFont("CaveatBrush-Regular.ttf", "CaveatBrush");
				fonts.AddFont("DMSerifDisplay-Regular.ttf", "DMSerif");
				fonts.AddFont("SpaceMono-Bold.ttf", "SpaceMono");
				fonts.AddFont("Pacifico-Regular.ttf", "Pacifico");
			});

		// Entry/Editor sit in their own frame (style "Field"), so no native line/frame
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
#endif // Windows: siehe Platforms/Windows/App.xaml
	}
}
