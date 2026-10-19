using Bubble.Core;

namespace Bubble.Tests;

/// <summary>Läuft nur, wenn BUBBLE_RELAY_URL gesetzt ist, z. B. http://192.168.1.20:5080/</summary>
public sealed class LiveRelayFactAttribute : FactAttribute
{
    public static string? Url => Environment.GetEnvironmentVariable("BUBBLE_RELAY_URL");

    public LiveRelayFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Url)) Skip = "BUBBLE_RELAY_URL nicht gesetzt.";
    }
}

/// <summary>Ende-zu-Ende gegen einen echten Relay. Nutzt 24h-Posts, damit auf dem Server nichts liegen bleibt.</summary>
public class LiveRelayTests
{
    static readonly string Url = LiveRelayFactAttribute.Url?.TrimEnd('/') + "/";

    static RelayClient Relay(string url) => new(new HttpClient { BaseAddress = new Uri(url) });

    [LiveRelayFact]
    public async Task TwoMembers_ShareHighlights_AndRelaySeesOnlyCiphertext()
    {
        var anna = Vault.Create("Anna");
        var ben = Vault.Create("Ben");
        var bubble = BubbleMembership.Create("Live-Test", Url, PostLifetime.Story24h);
        Assert.True(Invite.TryParse(Invite.For(bubble).ToLink(), out var invite));
        var bensBubble = invite!.ToMembership();

        await new BubbleClient(anna, Relay).PostAsync(bubble, Mood.Top, "Hallo vom Live-Test", [0xFF, 0xD8, 0xFF]);
        await new BubbleClient(ben, Relay).PostAsync(bensBubble, Mood.Flop, "Regen", null);

        var annasFeed = await new BubbleClient(anna, Relay).LoadFeedAsync(bubble);
        Assert.Equal(["Regen", "Hallo vom Live-Test"], annasFeed.Select(p => p.Message.Text));

        var raw = await Relay(Url).GetMessagesAsync(bubble.MailboxId);
        Assert.Equal(2, raw.Count);
        Assert.All(raw, r => Assert.NotNull(r.ExpiresAt));
        var rawText = string.Join("", raw.Select(r => r.Ciphertext));
        Assert.DoesNotContain("Hallo", rawText);
        Assert.DoesNotContain(Convert.ToBase64String("Anna"u8.ToArray()), rawText);
    }

    [LiveRelayFact]
    public async Task Backup_RestoresOnNewDevice()
    {
        var vault = Vault.Create("Live-Backup");
        vault.Bubbles.Add(BubbleMembership.Create("WG", Url, PostLifetime.Story24h));
        var code = Backup.GenerateCode();
        vault.Backup = Backup.Create(code);
        await Relay(Url).PutBackupAsync(vault.Backup, Backup.Seal(vault.Backup, vault));

        var secrets = Backup.DeriveFromCode(code);
        var blob = await Relay(Url).GetBackupAsync(secrets.BackupId, secrets.AuthToken);
        var restored = Backup.Open(secrets, blob!);
        Assert.Equal(vault.Identity.PublicKey, restored.Identity.PublicKey);
    }
}
