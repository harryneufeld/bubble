using Bubble.App.Controls;

namespace Bubble.App.Pages;

public partial class AboutPage : ContentPage
{
    public AboutPage()
    {
        InitializeComponent();
        VersionLabel.Text = $"Version {AppInfo.Current.VersionString} ({AppInfo.Current.BuildString})";
    }

    async void OnBubbleWebsite(object? sender, TappedEventArgs e) => await OpenAsync("https://bubble-social.org");

    async void OnGitHub(object? sender, TappedEventArgs e) => await OpenAsync("https://github.com/harryneufeld/bubble");

    async void OnAuthor(object? sender, TappedEventArgs e) => await OpenAsync("https://harry-neufeld.de");

    async Task OpenAsync(string url)
    {
        try
        {
            if (!await Launcher.Default.OpenAsync(new Uri(url)))
                await this.ShowAlertAsync("Link not opened", $"Please open {url} in your browser yourself.", "OK");
        }
        catch (Exception)
        {
            // no browser found or not allowed
            await this.ShowAlertAsync("Link not opened", $"Please open {url} in your browser yourself.", "OK");
        }
    }
}
