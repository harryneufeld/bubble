using Bubble.App.Controls;
using Bubble.App.Services;

namespace Bubble.App.Pages;

public partial class ProfileEditPage : ContentPage
{
    readonly AccountService _account;
    byte[]? _avatar;

    public ProfileEditPage(AccountService account)
    {
        InitializeComponent();
        _account = account;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        var vault = _account.Vault!;
        _avatar = vault.Avatar;
        NameEntry.Text = vault.DisplayName;
        BioEntry.Text = vault.Bio;
        AvatarPreview.Name = vault.DisplayName;
        AvatarPreview.Image = _avatar;
    }

    void OnNameChanged(object? sender, TextChangedEventArgs e) => AvatarPreview.Name = e.NewTextValue ?? "";

    async void OnPickAvatar(object? sender, TappedEventArgs e)
    {
        var photo = await PhotoPicker.PickAsync(this, "Profile picture", _avatar is not null, () =>
        {
            _avatar = null;
            AvatarPreview.Image = null;
        });
        if (photo is null) return;
        try
        {
            await using var stream = await photo.OpenReadAsync();
            _avatar = await ImageSanitizer.SanitizeAvatarAsync(stream);
            AvatarPreview.Image = _avatar;
        }
        catch (InvalidDataException)
        {
            await this.ShowAlertAsync("Photo not readable", "Unfortunately this image format is not supported.", "OK");
        }
    }

    async void OnSave(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameEntry.Text))
        {
            await this.ShowAlertAsync("Name missing", "What should your people call you?", "OK");
            return;
        }
        SaveButton.IsEnabled = false;
        try
        {
            if (!await _account.UpdateProfileAsync(NameEntry.Text, BioEntry.Text, _avatar))
                await this.ShowAlertAsync("Not delivered everywhere", "At least one bubble was not reachable. It will see your new profile the next time you share something there.", "OK");
            await Shell.Current.GoToAsync("..");
        }
        finally
        {
            SaveButton.IsEnabled = true;
        }
    }
}
