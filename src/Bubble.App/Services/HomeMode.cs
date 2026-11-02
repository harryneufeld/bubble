namespace Bubble.App.Services;

/// <summary>How the home screen looks back at kept highlights. Stored per device, not in the vault.</summary>
public enum HomeLayout { Classic, Box }

public static class HomeMode
{
    const string Key = "bubble.home.mode";

    public static HomeLayout Current
    {
        get => Preferences.Default.Get(Key, nameof(HomeLayout.Classic)) == nameof(HomeLayout.Box) ? HomeLayout.Box : HomeLayout.Classic;
        set => Preferences.Default.Set(Key, value.ToString());
    }
}

/// <summary>Decorative movement stops when the system has animations switched off.</summary>
public static class Motion
{
    public static bool IsReduced
    {
        get
        {
#if ANDROID
            return Android.Provider.Settings.Global.GetFloat(Platform.AppContext.ContentResolver, Android.Provider.Settings.Global.AnimatorDurationScale, 1f) == 0f;
#else
            return false;
#endif
        }
    }
}
