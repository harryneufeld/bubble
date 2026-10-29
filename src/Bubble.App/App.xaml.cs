using Microsoft.Extensions.DependencyInjection;

namespace Bubble.App;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();
		UserAppTheme = AppTheme.Dark; // Bubble ist bewusst dunkel (DESIGN.md)
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(new AppShell());
	}
}