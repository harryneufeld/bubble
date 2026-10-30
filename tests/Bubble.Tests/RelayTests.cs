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

        await _relay.ClientFor(anna).PostAsync(bubble, "😊", "Sunrise at the lake", [1, 2, 3]);

        var feed = await _relay.ClientFor(ben).LoadFeedAsync(bensBubble);
        var post = Assert.Single(feed).Message;
        Assert.Equal("Anna", post.AuthorName);
        Assert.Equal("😊", post.Emoji);
        Assert.Equal([1, 2, 3], post.Image);
        Assert.False(feed[0].IsMine);

        var raw = await _relay.Http().GetStringAsync($"v1/mailboxes/{bubble.MailboxId}/messages");
        Assert.DoesNotContain("Sunrise", raw);
        Assert.DoesNotContain("Anna", raw);
    }

    [Fact]
    public async Task MultiplePostsPerDay_AreAllowed()
    {
        var anna = Vault.Create("Anna");
        var bubble = BubbleMembership.Create("WG", "http://relay/", PostLifetime.Persistent);
        var client = _relay.ClientFor(anna);

        await client.PostAsync(bubble, "😕", "Missed the bus", null);
        _relay.Time.Advance(TimeSpan.FromMinutes(5));
        await client.PostAsync(bubble, "😊", "Got a free coffee for it", null);

        var feed = await client.LoadFeedAsync(bubble);
        Assert.Equal(["Got a free coffee for it", "Missed the bus"], feed.Select(p => p.Message.Text));
    }

    static async Task<(Vault Anna, Vault Ben, Vault Cara, BubbleMembership Annas, BubbleMembership Bens, BubbleMembership Caras)> Trio(RelayFixture relay)
    {
        var anna = Vault.Create("Anna");
        var ben = Vault.Create("Ben");
        var cara = Vault.Create("Cara");
        var annas = BubbleMembership.Create("WG", "http://relay/", PostLifetime.Persistent, anna.Identity.PublicKey);
        var bens = Invite.For(annas).ToMembership();
        var caras = Invite.For(annas).ToMembership();
        await relay.ClientFor(anna).PublishProfileAsync(annas);
        await relay.ClientFor(ben).PublishProfileAsync(bens);
        await relay.ClientFor(cara).PublishProfileAsync(caras);
        return (anna, ben, cara, annas, bens, caras);
    }

    [Fact]
    public void SharedSecret_IsSymmetric()
    {
        var a = Identity.Create();
        var b = Identity.Create();
        Assert.Equal(a.SharedSecretWith(b.PublicKey), b.SharedSecretWith(a.PublicKey));
    }

    [Fact]
    public async Task RemovedMember_LosesAccess_WhileOthersFollowTheNewKey()
    {
        var (anna, ben, cara, annas, bens, caras) = await Trio(_relay);
        var oldMailbox = annas.MailboxId;

        await _relay.ClientFor(anna).RemoveMemberAsync(annas, ben.Identity.PublicKey);
        Assert.NotEqual(oldMailbox, annas.MailboxId);

        var carasSnapshot = await _relay.ClientFor(cara).LoadAsync(caras);
        Assert.False(carasSnapshot.Removed);
        Assert.Equal(annas.MailboxId, caras.MailboxId);
        Assert.Equal(["Anna", "Cara"], carasSnapshot.Members.Select(m => m.Name).Order());

        var bensSnapshot = await _relay.ClientFor(ben).LoadAsync(bens);
        Assert.True(bensSnapshot.Removed);
        Assert.Equal(oldMailbox, bens.MailboxId);

        await _relay.ClientFor(cara).PostAsync(caras, "😊", "after removal", null);
        Assert.Equal("after removal", Assert.Single(await _relay.ClientFor(anna).LoadFeedAsync(annas)).Message.Text);
    }

    [Fact]
    public async Task OnlyOwner_CanRemoveMembers()
    {
        var (_, ben, cara, _, bens, _) = await Trio(_relay);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _relay.ClientFor(ben).RemoveMemberAsync(bens, cara.Identity.PublicKey));
    }

    [Fact]
    public async Task MemberWhoLeft_DisappearsFromTheMemberList()
    {
        var (anna, ben, _, annas, bens, _) = await Trio(_relay);
        _relay.Time.Advance(TimeSpan.FromSeconds(1));
        await _relay.ClientFor(ben).LeaveAsync(bens);

        var snapshot = await _relay.ClientFor(anna).LoadAsync(annas);
        Assert.DoesNotContain(snapshot.Members, m => m.Name == "Ben");
    }

    [Fact]
    public async Task StoryPostsExpire_PersistentPostsStay_AndLifetimeChangeSyncs()
    {
        var anna = Vault.Create("Anna");
        var ben = Vault.Create("Ben");
        var bubble = BubbleMembership.Create("Family", "http://relay/", PostLifetime.Story24h);
        var bensBubble = Invite.For(bubble).ToMembership();

        await _relay.ClientFor(anna).PostAsync(bubble, "😊", "disappears", null);
        await _relay.ClientFor(anna).SetLifetimeAsync(bubble, PostLifetime.Persistent);

        // The app loads the feed before sharing and adopts the current lifetime.
        var feed = await _relay.ClientFor(ben).LoadFeedAsync(bensBubble);
        Assert.Equal(PostLifetime.Persistent, bensBubble.Lifetime);
        await _relay.ClientFor(ben).PostAsync(bensBubble, "😊", "stays", null);

        feed = await _relay.ClientFor(ben).LoadFeedAsync(bensBubble);
        Assert.Equal(PostLifetime.Story24h, feed.Single(p => p.Message.Text == "disappears").Message.Lifetime);

        _relay.Time.Advance(TimeSpan.FromHours(25));
        feed = await _relay.ClientFor(ben).LoadFeedAsync(bensBubble);
        Assert.Equal("stays", Assert.Single(feed).Message.Text);
    }

    [Fact]
    public async Task ReplayedOrTamperedMessage_IsRejectedByClient()
    {
        var anna = Vault.Create("Anna");
        var bubble = BubbleMembership.Create("A", "http://relay/", PostLifetime.Persistent);
        await _relay.ClientFor(anna).PostAsync(bubble, "😊", "hi", null);
        var raw = Assert.Single(await _relay.Relay("").GetMessagesAsync(bubble.MailboxId));

        // Server replays the same message under a new ID.
        Assert.Null(MessageCodec.Decrypt(bubble, raw with { Id = Base64Url.Encode(Crypto.RandomBytes(16)) }));

        // Server changes a byte in the ciphertext.
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

        var badId = await http.PostAsJsonAsync("v1/mailboxes/too-short/messages", new RelayMessageIn("aaaaaaaaaaaaaaaa", "AQID", null));
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

        // New device: only the code is known.
        var secrets = Backup.DeriveFromCode(code);
        var blob = await _relay.Relay("").GetBackupAsync(secrets.BackupId, secrets.AuthToken);
        var restored = Backup.Open(code, blob!);
        Assert.Equal(vault.Identity.PublicKey, restored.Identity.PublicKey);
        Assert.Equal("WG", Assert.Single(restored.Bubbles).Name);

        // Wrong token: neither read nor overwrite.
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
        await _relay.ClientFor(ben).PostAsync(bubble, "😊", "Hallo", null);
        _relay.Time.Advance(TimeSpan.FromMinutes(1));
        anna.DisplayName = "Anna K.";
        await _relay.ClientFor(anna).PublishProfileAsync(bubble);

        var snapshot = await _relay.ClientFor(ben).LoadAsync(bubble);
        Assert.Single(snapshot.Posts); // profiles are not posts
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

    [Fact]
    public async Task Likes_AreToggledPerPerson_AndNewestWins()
    {
        var anna = Vault.Create("Anna");
        var ben = Vault.Create("Ben");
        var bubble = BubbleMembership.Create("WG", "http://relay/", PostLifetime.Persistent);
        await _relay.ClientFor(anna).PostAsync(bubble, "😍", "Neue Schuhe", null);

        var annasClient = _relay.ClientFor(anna);
        var bensClient = _relay.ClientFor(ben);
        var post = (await bensClient.LoadFeedAsync(bubble)).Single();

        await bensClient.LikeAsync(bubble, post, true);
        _relay.Time.Advance(TimeSpan.FromMinutes(1));
        await annasClient.LikeAsync(bubble, post, true);
        _relay.Time.Advance(TimeSpan.FromMinutes(1));
        await bensClient.LikeAsync(bubble, post, false); // heart taken back
        _relay.Time.Advance(TimeSpan.FromMinutes(1));
        await bensClient.LikeAsync(bubble, post, true);  // and set again

        var snapshot = await annasClient.LoadAsync(bubble);
        Assert.Equal(["Anna", "Ben"], snapshot.LikersOf(post.Id).Select(m => m.Name == "?" ? "?" : m.Name));
        Assert.True(snapshot.IsLikedByMe(post.Id));
        Assert.Single(snapshot.Posts); // hearts are not posts
    }

    [Fact]
    public async Task Comments_AreSigned_ShownInOrder_AndExpireWithTheirPost()
    {
        var anna = Vault.Create("Anna");
        var ben = Vault.Create("Ben");
        var bubble = BubbleMembership.Create("WG", "http://relay/", PostLifetime.Story24h);
        await _relay.ClientFor(anna).PublishProfileAsync(bubble);
        await _relay.ClientFor(ben).PublishProfileAsync(bubble);
        await _relay.ClientFor(anna).PostAsync(bubble, "🥳", "Wir ziehen um!", null);

        var post = (await _relay.ClientFor(ben).LoadFeedAsync(bubble)).Single();
        var first = await _relay.ClientFor(ben).CommentAsync(bubble, post, "  Congratulations!  ");
        Assert.Equal("Congratulations!", first.Text);
        _relay.Time.Advance(TimeSpan.FromMinutes(2));
        await _relay.ClientFor(anna).CommentAsync(bubble, post, "Thank you");

        var snapshot = await _relay.ClientFor(anna).LoadAsync(bubble);
        Assert.Equal(["Congratulations!", "Thank you"], snapshot.CommentsOf(post.Id).Select(c => c.Text));
        Assert.Equal(["Ben", "Anna"], snapshot.CommentsOf(post.Id).Select(c => c.AuthorName));

        // After 24 hours the post, hearts and comments disappear – on the relay too
        _relay.Time.Advance(TimeSpan.FromHours(25));
        snapshot = await _relay.ClientFor(anna).LoadAsync(bubble);
        Assert.Empty(snapshot.Posts);
        Assert.Empty(snapshot.CommentsOf(post.Id));
        var raw = await _relay.Http().GetStringAsync($"v1/mailboxes/{bubble.MailboxId}/messages");
        Assert.DoesNotContain("Congratulations", raw);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _relay.ClientFor(ben).CommentAsync(bubble, post, "too late"));
    }

    [Fact]
    public async Task TextOnlyPost_HasNoImage_NotAnEmptyOne()
    {
        var anna = Vault.Create("Anna");
        var bubble = BubbleMembership.Create("WG", "http://relay/", PostLifetime.Persistent);
        await _relay.ClientFor(anna).PostAsync(bubble, "😤", "Autopanne", null);

        var post = (await _relay.ClientFor(anna).LoadFeedAsync(bubble)).Single().Message;
        Assert.Null(post.Image); // ein leeres Array würde als Foto-Post mit leerem Rahmen gezeigt
    }
}
