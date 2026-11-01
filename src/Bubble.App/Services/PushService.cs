using System.Diagnostics;
using System.Text.Json;
using Bubble.Core;

namespace Bubble.App.Services;

/// <summary>
/// Push notifications: keeps the relays' push subscriptions in line with the bubbles, and turns a wake into a notification.
/// A wake carries no content – the posts are fetched from the relay and decrypted here, on the device.
/// </summary>
public sealed class PushService(AccountService account)
{
    const string EnabledKey = "bubble.push.enabled";

    readonly PushRegistrar _registrar = new(account.RelayFor, new SecureStateStore());
    bool _started;

    /// <summary>Whether this build can do push at all (it needs the Firebase settings from push.props).</summary>
    public static bool IsAvailable => PushPlatform.IsSupported();

    /// <summary>Notifications are opt-in.</summary>
    public static bool IsEnabled => IsAvailable && Preferences.Default.Get(EnabledKey, false);

    /// <summary>Call once when the app starts.</summary>
    public void Start()
    {
        if (_started) return;
        _started = true;
        if (!IsAvailable) return;
        account.Saved += () => _ = SyncAsync();
        _ = Task.Run(async () =>
        {
            if (await account.LoadAsync()) await SyncAsync();
        });
    }

    /// <summary>Switches notifications on or off. False if they could not be switched on (permission refused, no Google services, server unreachable).</summary>
    public async Task<bool> SetEnabledAsync(bool enabled)
    {
        if (!enabled)
        {
            Preferences.Default.Set(EnabledKey, false);
            return await SyncAsync();
        }

        if (!IsAvailable || !await PushPlatform.RequestNotificationPermissionAsync()) return false;
        Preferences.Default.Set(EnabledKey, true);
        if (await SyncAsync()) return true;
        Preferences.Default.Set(EnabledKey, false);
        await SyncAsync();
        return false;
    }

    /// <summary>Follows the bubble list and the device token. Never throws: it runs in the background.</summary>
    public async Task<bool> SyncAsync()
    {
        try
        {
            var vault = account.Vault;
            if (vault is null) return true;
            return await _registrar.SyncAsync(IsEnabled ? PushPlatform.CreateProvider() : null, vault.Bubbles.ToList());
        }
        catch (Exception e)
        {
            Debug.WriteLine($"Push sync failed: {e.Message}");
            return false;
        }
    }

    /// <summary>Removes all push subscriptions, e.g. before logging out.</summary>
    public async Task RemoveAllAsync()
    {
        try { await _registrar.SyncAsync(null, []); }
        catch (Exception e) { Debug.WriteLine($"Push cleanup failed: {e.Message}"); }
    }

    /// <summary>A wake arrived: look for posts nobody has seen yet and announce them. Called from the push service on Android.</summary>
    public async Task HandleWakeAsync()
    {
        try
        {
            if (!IsEnabled || !await account.LoadAsync()) return;
            var known = NotifiedPosts.Load();
            var keysChanged = false;

            foreach (var bubble in account.Vault!.Bubbles.ToList())
            {
                var before = bubble.Key;
                BubbleSnapshot snapshot;
                try { snapshot = await account.Client.LoadAsync(bubble); }
                catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { continue; }
                keysChanged |= before != bubble.Key;
                if (snapshot.Removed) continue;

                var fresh = known.TryGetValue(bubble.MailboxId, out var ids)
                    ? PostNotifications.NewEvents(snapshot, ids).Where(e => NotificationPreferences.IsOn(e.Kind)).ToList()
                    : [];
                // Everything counts as announced, also what the settings filtered out, and the first time a bubble is seen its backlog.
                known[bubble.MailboxId] = PostNotifications.KnownKeys(snapshot);
                if (fresh.Count == 0) continue;

                PushPlatform.ShowNotification(bubble.MailboxId, bubble.Name, PostNotifications.Summarize(fresh));
            }

            NotifiedPosts.Save(known);
            if (keysChanged) await account.SaveAsync();
        }
        catch (Exception e)
        {
            Debug.WriteLine($"Push wake failed: {e.Message}");
        }
    }

    sealed class SecureStateStore : IPushStateStore
    {
        const string Key = "bubble.push.state";

        public Task<string?> GetAsync() => SecureStorage.Default.GetAsync(Key);

        public Task SetAsync(string value) => SecureStorage.Default.SetAsync(Key, value);

        public Task ClearAsync()
        {
            SecureStorage.Default.Remove(Key);
            return Task.CompletedTask;
        }
    }
}

/// <summary>
/// Remembers per bubble which posts the user has already seen or been told about (message IDs only, no content),
/// so a wake announces only what is really new.
/// </summary>
public static class NotifiedPosts
{
    const string Key = "bubble.push.notified";

    public static Dictionary<string, HashSet<string>> Load()
    {
        try { return JsonSerializer.Deserialize<Dictionary<string, HashSet<string>>>(Preferences.Default.Get(Key, "{}")) ?? []; }
        catch (JsonException) { return []; }
    }

    public static void Save(Dictionary<string, HashSet<string>> known) =>
        Preferences.Default.Set(Key, JsonSerializer.Serialize(known));

    /// <summary>Everything the feed shows counts as seen.</summary>
    public static void MarkSeen(string mailboxId, BubbleSnapshot snapshot)
    {
        if (!PushService.IsEnabled) return;
        var known = Load();
        known[mailboxId] = PostNotifications.KnownKeys(snapshot);
        Save(known);
    }
}

/// <summary>Which kinds of notifications the user wants. All are on by default.</summary>
public static class NotificationPreferences
{
    public static bool IsOn(NotificationKind kind) => Preferences.Default.Get(Key(kind), true);

    public static void Set(NotificationKind kind, bool on) => Preferences.Default.Set(Key(kind), on);

    static string Key(NotificationKind kind) => $"bubble.push.kind.{kind}";
}
