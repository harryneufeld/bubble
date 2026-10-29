using System.Net;
using System.Net.Http.Json;
using Bubble.Core;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace Bubble.Tests;

public sealed class RelayFixture : IDisposable
{
    readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"bubble-relay-{Guid.NewGuid():N}.db");
    readonly WebApplicationFactory<Program> _factory;

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 10, 19, 10, 0, 0, TimeSpan.Zero));

    public RelayFixture()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Relay:Database", _dbPath);
            b.ConfigureServices(s => s.Replace(ServiceDescriptor.Singleton<TimeProvider>(Time)));
        });
    }

    public HttpClient Http() => _factory.CreateClient();

    public RelayClient Relay(string _) => new(Http());

    public BubbleClient ClientFor(Vault vault) => new(vault, Relay, Time);

    public void Dispose()
    {
        _factory.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var f in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
            File.Delete(f);
    }
}

public class RelayTests : IDisposable
{
    readonly RelayFixture _relay = new();

    public void Dispose() => _relay.Dispose();

    [Fact]
    public async Task InvitedMember_SeesPost_WhileRelayStoresOnlyCiphertext()
    {
        var anna = Vault.Create("Anna");
        var ben = Vault.Create("Ben");
        var bubble = BubbleMembership.Create("WG", "http://relay/", PostLifetime.Story24h);
        Assert.True(Invite.TryParse(Invite.For(bubble).ToLink(), out var invite));
        var bensBubble = invite!.ToMembership();

        await _relay.ClientFor(anna).PostAsync(bubble, Mood.Top, "Sonnenaufgang am See", [1, 2, 3]);

        var feed = await _relay.ClientFor(ben).LoadFeedAsync(bensBubble);
        var post = Assert.Single(feed).Message;
        Assert.Equal("Anna", post.AuthorName);
        Assert.Equal(Mood.Top, post.Mood);
        Assert.Equal([1, 2, 3], post.Image);
        Assert.False(feed[0].IsMine);

        var raw = await _relay.Http().GetStringAsync($"v1/mailboxes/{bubble.MailboxId}/messages");
        Assert.DoesNotContain("Sonnenaufgang", raw);
        Assert.DoesNotContain("Anna", raw);
    }

    [Fact]
    public async Task MultiplePostsPerDay_AreAllowed()
    {
        var anna = Vault.Create("Anna");
        var bubble = BubbleMembership.Create("WG", "http://relay/", PostLifetime.Persistent);
        var client = _relay.ClientFor(anna);

        await client.PostAsync(bubble, Mood.Flop, "Bus verpasst", null);
        _relay.Time.Advance(TimeSpan.FromMinutes(5));
        await client.PostAsync(bubble, Mood.Top, "Dafür Kaffee geschenkt bekommen", null);

        var feed = await client.LoadFeedAsync(bubble);
        Assert.Equal(["Dafür Kaffee geschenkt bekommen", "Bus verpasst"], feed.Select(p => p.Message.Text));
    }

    [Fact]
    public async Task StoryPostsExpire_PersistentPostsStay_AndLifetimeChangeSyncs()
    {
        var anna = Vault.Create("Anna");
        var ben = Vault.Create("Ben");
        var bubble = BubbleMembership.Create("Familie", "http://relay/", PostLifetime.Story24h);
        var bensBubble = Invite.For(bubble).ToMembership();

        await _relay.ClientFor(anna).PostAsync(bubble, Mood.Top, "verschwindet", null);
        await _relay.ClientFor(anna).SetLifetimeAsync(bubble, PostLifetime.Persistent);

        // Die App lädt den Feed vor dem Teilen und übernimmt dabei die aktuelle Lebensdauer.
        var feed = await _relay.ClientFor(ben).LoadFeedAsync(bensBubble);
        Assert.Equal(PostLifetime.Persistent, bensBubble.Lifetime);
        await _relay.ClientFor(ben).PostAsync(bensBubble, Mood.Top, "bleibt", null);

        feed = await _relay.ClientFor(ben).LoadFeedAsync(bensBubble);
        Assert.Equal(PostLifetime.Story24h, feed.Single(p => p.Message.Text == "verschwindet").Message.Lifetime);

        _relay.Time.Advance(TimeSpan.FromHours(25));
        feed = await _relay.ClientFor(ben).LoadFeedAsync(bensBubble);
        Assert.Equal("bleibt", Assert.Single(feed).Message.Text);
    }

    [Fact]
    public async Task ReplayedOrTamperedMessage_IsRejectedByClient()
    {
        var anna = Vault.Create("Anna");
        var bubble = BubbleMembership.Create("A", "http://relay/", PostLifetime.Persistent);
        await _relay.ClientFor(anna).PostAsync(bubble, Mood.Top, "hi", null);
        var raw = Assert.Single(await _relay.Relay("").GetMessagesAsync(bubble.MailboxId));

        // Server spielt dieselbe Nachricht unter neuer ID erneut ein.
        Assert.Null(MessageCodec.Decrypt(bubble, raw with { Id = Base64Url.Encode(Crypto.RandomBytes(16)) }));

        // Server verändert ein Byte im Ciphertext.
        var bytes = Convert.FromBase64String(raw.Ciphertext);
        bytes[^1] ^= 1;
        Assert.Null(MessageCodec.Decrypt(bubble, raw with { Ciphertext = Convert.ToBase64String(bytes) }));

        Assert.Single(await _relay.ClientFor(anna).LoadFeedAsync(bubble));
    }

    [Fact]
    public async Task Relay_RejectsInvalidInput()
    {
        var http = _relay.Http();
        var mailbox = Base64Url.Encode(Crypto.NewKey());

        var badId = await http.PostAsJsonAsync("v1/mailboxes/zu-kurz/messages", new RelayMessageIn("aaaaaaaaaaaaaaaa", "AQID", null));
        Assert.Equal(HttpStatusCode.BadRequest, badId.StatusCode);

        var tooLong = _relay.Time.GetUtcNow().AddDays(3).ToUnixTimeSeconds();
        var badExpiry = await http.PostAsJsonAsync($"v1/mailboxes/{mailbox}/messages", new RelayMessageIn("aaaaaaaaaaaaaaaa", "AQID", tooLong));
        Assert.Equal(HttpStatusCode.BadRequest, badExpiry.StatusCode);
    }

    [Fact]
    public async Task Backup_RestoresOnNewDevice_AndCannotBeOverwrittenByOthers()
    {
        var vault = Vault.Create("Anna");
        vault.Bubbles.Add(BubbleMembership.Create("WG", "http://relay/", PostLifetime.Story24h));
        var code = Backup.GenerateCode();
        vault.Backup = Backup.Create(code);
        await _relay.Relay("").PutBackupAsync(vault.Backup, Backup.Seal(vault.Backup, vault));

        // Neues Gerät: nur der Code ist bekannt.
        var secrets = Backup.DeriveFromCode(code);
        var blob = await _relay.Relay("").GetBackupAsync(secrets.BackupId, secrets.AuthToken);
        var restored = Backup.Open(code, blob!);
        Assert.Equal(vault.Identity.PublicKey, restored.Identity.PublicKey);
        Assert.Equal("WG", Assert.Single(restored.Bubbles).Name);

        // Falscher Token: weder lesen noch überschreiben.
        var attacker = vault.Backup with { AuthToken = Base64Url.Encode(Crypto.NewKey()) };
        Assert.Null(await _relay.Relay("").GetBackupAsync(attacker.BackupId, attacker.AuthToken));
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => _relay.Relay("").PutBackupAsync(attacker, [1]));
        Assert.Equal(HttpStatusCode.Forbidden, ex.StatusCode);
    }

    [Fact]
    public async Task Profile_IsSharedEncrypted_AndNewestWins()
    {
        var anna = Vault.Create("Anna");
        var ben = Vault.Create("Ben");
        var bubble = BubbleMembership.Create("WG", "http://relay/", PostLifetime.Story24h);

        anna.Avatar = [9, 9, 9];
        anna.Bio = "Kaffee zuerst";
        await _relay.ClientFor(anna).PublishProfileAsync(bubble);
        await _relay.ClientFor(ben).PostAsync(bubble, Mood.Top, "Hallo", null);
        _relay.Time.Advance(TimeSpan.FromMinutes(1));
        anna.DisplayName = "Anna K.";
        await _relay.ClientFor(anna).PublishProfileAsync(bubble);

        var snapshot = await _relay.ClientFor(ben).LoadAsync(bubble);
        Assert.Single(snapshot.Posts); // Profile sind keine Posts
        Assert.Equal(2, snapshot.Members.Count);
        Assert.True(snapshot.Members[0].IsMe);
        var annaSeen = snapshot.MemberFor(anna.Identity.PublicKey)!;
        Assert.Equal("Anna K.", annaSeen.Name);
        Assert.Equal([9, 9, 9], annaSeen.Avatar);
        Assert.Equal("Kaffee zuerst", annaSeen.Bio);

        var raw = await _relay.Http().GetStringAsync($"v1/mailboxes/{bubble.MailboxId}/messages");
        Assert.DoesNotContain("Kaffee", raw);
    }

    [Fact]
    public async Task ShortInvite_ResolvesToBubble_AndExpires()
    {
        var bubble = BubbleMembership.Create("Laufgruppe", "http://relay/", PostLifetime.Persistent);
        var code = await ShortInvite.PublishAsync(Invite.For(bubble), _relay.Relay(""), _relay.Time);
        Assert.Matches("^[0-9A-Z]{5}-[0-9A-Z]{5}$", code);

        var invite = await ShortInvite.ResolveAsync(code.ToLowerInvariant().Replace("-", " "), _relay.Relay(""));
        Assert.Equal(bubble.MailboxId, invite!.ToMembership().MailboxId);
        Assert.Equal("Laufgruppe", invite.Name);

        Assert.Null(await ShortInvite.ResolveAsync(ShortInvite.GenerateCode(), _relay.Relay("")));

        _relay.Time.Advance(ShortInvite.Validity + TimeSpan.FromMinutes(1));
        Assert.Null(await ShortInvite.ResolveAsync(code, _relay.Relay("")));
    }
}
