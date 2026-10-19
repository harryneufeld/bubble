using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Bubble.Core;

namespace Bubble.Tests;

static class TestSetup
{
    // Argon2 im Test billig machen; die echten Parameter gelten in App und Relay.
    [ModuleInitializer]
    internal static void Init()
    {
        Backup.Argon2MemoryKiB = 1024;
        Backup.Argon2Iterations = 1;
    }
}

public class CryptoTests
{
    [Fact]
    public void SealOpen_RoundTrips()
    {
        var key = Crypto.NewKey();
        var sealedData = Crypto.Seal(key, "hallo"u8.ToArray(), "ctx");
        Assert.Equal("hallo", Encoding.UTF8.GetString(Crypto.Open(key, sealedData, "ctx")));
    }

    [Fact]
    public void Open_FailsWithWrongContextOrKey()
    {
        var key = Crypto.NewKey();
        var sealedData = Crypto.Seal(key, "hallo"u8.ToArray(), "ctx");
        Assert.Throws<CryptographicException>(() => Crypto.Open(key, sealedData, "anderer-ctx"));
        Assert.Throws<CryptographicException>(() => Crypto.Open(Crypto.NewKey(), sealedData, "ctx"));
    }

    [Fact]
    public void Signature_DetectsTampering()
    {
        var identity = Identity.Create();
        var message = "highlight"u8.ToArray();
        var signature = identity.Sign(message);
        Assert.True(Identity.Verify(identity.PublicKey, message, signature));
        Assert.False(Identity.Verify(identity.PublicKey, "lowlight"u8.ToArray(), signature));
        Assert.False(Identity.Verify(Identity.Create().PublicKey, message, signature));
    }
}

public class InviteTests
{
    [Fact]
    public void Link_RoundTripsAndKeepsKeyInFragment()
    {
        var bubble = BubbleMembership.Create("WG", "http://localhost:5080/", PostLifetime.Persistent);
        var link = Invite.For(bubble).ToLink();

        Assert.StartsWith("bubble://join#", link);
        Assert.True(Invite.TryParse(link, out var invite));
        var joined = invite!.ToMembership();
        Assert.Equal(bubble.MailboxId, joined.MailboxId);
        Assert.Equal(PostLifetime.Persistent, joined.Lifetime);
    }

    [Theory]
    [InlineData("")]
    [InlineData("bubble://join#kaputt")]
    [InlineData("irgendwas")]
    public void TryParse_RejectsGarbage(string text) => Assert.False(Invite.TryParse(text, out _));
}

public class BackupTests
{
    [Fact]
    public void Code_HasReadableFormat()
    {
        var code = Backup.GenerateCode();
        Assert.Matches("^[0-9A-Z]{5}(-[0-9A-Z]{5}){4}$", code);
    }

    [Fact]
    public void NormalizeCode_ToleratesTypos()
    {
        Assert.Equal(Backup.NormalizeCode("ABCDE-0111A-BCDEF-GHJKM-NPQRS"),
                     Backup.NormalizeCode("abcde o1il a bcdef ghjkm npqrs"));
        Assert.Throws<FormatException>(() => Backup.NormalizeCode("zu-kurz"));
    }

    [Fact]
    public void SealOpen_RestoresVault()
    {
        var vault = Vault.Create("Mia");
        vault.Bubbles.Add(BubbleMembership.Create("Familie", "http://localhost:5080/", PostLifetime.Story24h));
        var code = Backup.GenerateCode();
        vault.Backup = Backup.Create(code);

        var restored = Backup.Open(code.ToLowerInvariant(), Backup.Seal(vault.Backup, vault));

        Assert.Equal(vault.IdentitySeed, restored.IdentitySeed);
        Assert.Equal("Familie", Assert.Single(restored.Bubbles).Name);
        Assert.Equal(vault.Backup, restored.Backup);
    }

    [Fact]
    public void Open_FailsWithWrongCode()
    {
        var vault = Vault.Create("Mia");
        vault.Backup = Backup.Create(Backup.GenerateCode());
        var blob = Backup.Seal(vault.Backup, vault);
        Assert.Throws<CryptographicException>(() => Backup.Open(Backup.GenerateCode(), blob));
    }
}
