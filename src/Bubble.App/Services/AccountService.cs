using System.Text.Json;
using Bubble.Core;

namespace Bubble.App.Services;

/// <summary>
/// Keeps the vault (identity + bubble keys) in the device's secure storage
/// (Android Keystore, iOS Keychain, Windows DPAPI) and keeps the backup up to date.
/// </summary>
public sealed class AccountService
{
    const string VaultKey = "bubble.vault.v1";
    readonly Dictionary<string, RelayClient> _relays = [];

    public Vault? Vault { get; private set; }

    /// <summary>Raised after the vault was saved, e.g. so push can follow the bubble list.</summary>
    public event Action? Saved;

    const string RelayUrlKey = "bubble.relay-url";

    /// <summary>Public relay. A different server (e.g. a local development relay) can be set under "Server".</summary>
    const string FallbackRelayUrl = "https://connect.bubble-social.org/";

    /// <summary>
    /// Relay for new bubbles and the backup. Joined bubbles use the relay from their invitation.
    /// </summary>
    public static string DefaultRelayUrl
    {
        get => Preferences.Default.Get(RelayUrlKey, FallbackRelayUrl);
        set => Preferences.Default.Set(RelayUrlKey, value);
    }

    /// <summary>Normalizes an entered server address, e.g. "192.168.1.20:5080" → "http://192.168.1.20:5080/".</summary>
    public static bool TryNormalizeRelayUrl(string? input, out string url)
    {
        url = "";
        var text = input?.Trim() ?? "";
        if (text.Length == 0) return false;
        if (!text.Contains("://")) text = "http://" + text;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return false;
        url = uri.GetLeftPart(UriPartial.Authority) + "/";
        return true;
    }

    /// <summary>The last loaded messages of every bubble, kept in the cache folder (the system may clear it; then it is just loaded again).</summary>
    public FileMessageCache Cache { get; } = new(Path.Combine(FileSystem.CacheDirectory, "messages"));

    public BubbleClient Client => new(Vault ?? throw new InvalidOperationException("No account."), RelayFor, cache: Cache);

    public async Task<bool> LoadAsync()
    {
        if (Vault is not null) return true;
        var json = await SecureStorage.Default.GetAsync(VaultKey);
        if (json is null) return false;
        Vault = JsonSerializer.Deserialize(json, CoreJson.Default.Vault);
        return Vault is not null;
    }

    public Task CreateAsync(string displayName, string? bio, byte[]? avatar)
    {
        Vault = Vault.Create(displayName.Trim());
        Vault.Bio = string.IsNullOrWhiteSpace(bio) ? null : bio.Trim();
        Vault.Avatar = avatar;
        return SaveAsync();
    }

    /// <summary>
    /// Forgets the account on this device. The backup stays on the server, so without a backup code
    /// the account (and every bubble key in it) is gone for good.
    /// </summary>
    public void LogOut()
    {
        Cache.Clear();
        SecureStorage.Default.Remove(VaultKey);
        Vault = null;
    }

    /// <summary>Saves the profile and sends it to all bubbles. False if at least one bubble was unreachable.</summary>
    public async Task<bool> UpdateProfileAsync(string displayName, string? bio, byte[]? avatar)
    {
        var vault = Vault ?? throw new InvalidOperationException("No account.");
        vault.DisplayName = displayName.Trim();
        vault.Bio = string.IsNullOrWhiteSpace(bio) ? null : bio.Trim();
        vault.Avatar = avatar;
        await SaveAsync();
        var results = await Task.WhenAll(vault.Bubbles.Select(TryPublishProfileAsync));
        return results.All(ok => ok);
    }

    /// <summary>Founds a bubble and introduces yourself in it. You become its owner.</summary>
    public async Task<BubbleMembership> CreateBubbleAsync(string name, PostLifetime lifetime)
    {
        var bubble = BubbleMembership.Create(name.Trim(), DefaultRelayUrl, lifetime, Vault!.Identity.PublicKey);
        Vault!.Bubbles.Add(bubble);
        await SaveAsync();
        await TryPublishProfileAsync(bubble);
        return bubble;
    }

    /// <summary>Joins a bubble. Null if you are already in it.</summary>
    public async Task<BubbleMembership?> JoinAsync(Invite invite)
    {
        var bubble = invite.ToMembership();
        if (FindBubble(bubble.MailboxId) is not null) return null;
        Vault!.Bubbles.Add(bubble);
        await SaveAsync();
        await TryPublishProfileAsync(bubble);
        return bubble;
    }

    /// <summary>Leaves a bubble: tells the others (if the relay is reachable) and forgets its key on this device.</summary>
    public async Task LeaveAsync(BubbleMembership bubble)
    {
        try { await Client.LeaveAsync(bubble); }
        catch (HttpRequestException) { /* leaving works offline; the others just keep listing you */ }
        await ForgetAsync(bubble);
    }

    /// <summary>Forgets a bubble on this device without telling anyone (e.g. after being removed).</summary>
    public async Task ForgetAsync(BubbleMembership bubble)
    {
        Vault!.Bubbles.Remove(bubble);
        Cache.Delete(bubble.MailboxId);
        await SaveAsync();
    }

    async Task<bool> TryPublishProfileAsync(BubbleMembership bubble)
    {
        try { await Client.PublishProfileAsync(bubble); return true; }
        catch (HttpRequestException) { return false; }
    }

    public BubbleMembership? FindBubble(string mailboxId) =>
        Vault?.Bubbles.FirstOrDefault(b => b.MailboxId == mailboxId);

    /// <summary>Saves locally and – if set up – updates the backup. False if the backup could not be uploaded.</summary>
    public async Task<bool> SaveAsync()
    {
        var vault = Vault ?? throw new InvalidOperationException("No account.");
        await SecureStorage.Default.SetAsync(VaultKey, JsonSerializer.Serialize(vault, CoreJson.Default.Vault));
        Saved?.Invoke();
        if (vault.Backup is null) return true;
        try
        {
            await RelayFor(DefaultRelayUrl).PutBackupAsync(vault.Backup, Backup.Seal(vault.Backup, vault));
            return true;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    /// <summary>Sets up a backup and returns the code the user has to keep safe.</summary>
    public async Task<string> EnableBackupAsync()
    {
        var vault = Vault ?? throw new InvalidOperationException("No account.");
        var code = Backup.GenerateCode();
        vault.Backup = await Task.Run(() => Backup.Create(code));
        if (!await SaveAsync())
            throw new HttpRequestException("The backup could not be uploaded.");
        return code;
    }

    /// <summary>Restores the account on this device from the backup. False for an unknown code.</summary>
    public async Task<bool> RestoreAsync(string code)
    {
        var secrets = await Task.Run(() => Backup.DeriveFromCode(code));
        var blob = await RelayFor(DefaultRelayUrl).GetBackupAsync(secrets.BackupId, secrets.AuthToken);
        if (blob is null) return false;
        Vault = await Task.Run(() => Backup.Open(secrets, blob));
        await SaveAsync();
        return true;
    }

    /// <summary>Checks whether a relay answers at the address.</summary>
    public static async Task<bool> IsReachableAsync(string url)
    {
        try
        {
            using var http = new HttpClient { BaseAddress = new Uri(url), Timeout = TimeSpan.FromSeconds(5) };
            using var response = await http.GetAsync("");
            return response.IsSuccessStatusCode;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    public RelayClient RelayFor(string url)
    {
        if (!_relays.TryGetValue(url, out var relay))
        {
            relay = new RelayClient(new HttpClient { BaseAddress = new Uri(url), Timeout = TimeSpan.FromSeconds(30) });
            _relays[url] = relay;
        }
        return relay;
    }
}
