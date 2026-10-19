using System.Text.Json;
using Bubble.Core;

namespace Bubble.App.Services;

/// <summary>
/// Hält den Vault (Identität + Bubble-Schlüssel) im sicheren Speicher des Geräts
/// (Android Keystore, iOS Keychain, Windows DPAPI) und hält das Backup aktuell.
/// </summary>
public sealed class AccountService
{
    const string VaultKey = "bubble.vault.v1";
    readonly Dictionary<string, RelayClient> _relays = [];

    public Vault? Vault { get; private set; }

    const string RelayUrlKey = "bubble.relay-url";

    /// <summary>Lokaler Entwicklungs-Relay. Der Android-Emulator erreicht den PC über 10.0.2.2.</summary>
    static string FallbackRelayUrl =>
        DeviceInfo.Platform == DevicePlatform.Android ? "http://10.0.2.2:5080/" : "http://localhost:5080/";

    /// <summary>
    /// Relay für neue Bubbles und das Backup. Beigetretene Bubbles nutzen den Relay aus ihrer Einladung.
    /// </summary>
    public static string DefaultRelayUrl
    {
        get => Preferences.Default.Get(RelayUrlKey, FallbackRelayUrl);
        set => Preferences.Default.Set(RelayUrlKey, value);
    }

    /// <summary>Normalisiert eine eingegebene Server-Adresse, z. B. „192.168.1.20:5080“ → „http://192.168.1.20:5080/“.</summary>
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

    public BubbleClient Client => new(Vault ?? throw new InvalidOperationException("Kein Account."), RelayFor);

    public async Task<bool> LoadAsync()
    {
        if (Vault is not null) return true;
        var json = await SecureStorage.Default.GetAsync(VaultKey);
        if (json is null) return false;
        Vault = JsonSerializer.Deserialize(json, CoreJson.Default.Vault);
        return Vault is not null;
    }

    public Task CreateAsync(string displayName)
    {
        Vault = Vault.Create(displayName.Trim());
        return SaveAsync();
    }

    public BubbleMembership? FindBubble(string mailboxId) =>
        Vault?.Bubbles.FirstOrDefault(b => b.MailboxId == mailboxId);

    /// <summary>Speichert lokal und aktualisiert – falls eingerichtet – das Backup. False, wenn das Backup nicht hochgeladen werden konnte.</summary>
    public async Task<bool> SaveAsync()
    {
        var vault = Vault ?? throw new InvalidOperationException("Kein Account.");
        await SecureStorage.Default.SetAsync(VaultKey, JsonSerializer.Serialize(vault, CoreJson.Default.Vault));
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

    /// <summary>Richtet ein Backup ein und gibt den Code zurück, den der Nutzer sicher aufbewahren muss.</summary>
    public async Task<string> EnableBackupAsync()
    {
        var vault = Vault ?? throw new InvalidOperationException("Kein Account.");
        var code = Backup.GenerateCode();
        vault.Backup = await Task.Run(() => Backup.Create(code));
        if (!await SaveAsync())
            throw new HttpRequestException("Backup konnte nicht hochgeladen werden.");
        return code;
    }

    /// <summary>Stellt den Account auf diesem Gerät aus dem Backup wieder her. False bei unbekanntem Code.</summary>
    public async Task<bool> RestoreAsync(string code)
    {
        var secrets = await Task.Run(() => Backup.DeriveFromCode(code));
        var blob = await RelayFor(DefaultRelayUrl).GetBackupAsync(secrets.BackupId, secrets.AuthToken);
        if (blob is null) return false;
        Vault = await Task.Run(() => Backup.Open(secrets, blob));
        await SaveAsync();
        return true;
    }

    /// <summary>Prüft, ob unter der Adresse ein Relay antwortet.</summary>
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
