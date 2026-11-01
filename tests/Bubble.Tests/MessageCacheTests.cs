using Bubble.Core;

namespace Bubble.Tests;

public class MessageCacheTests : IDisposable
{
    readonly RelayFixture _relay = new();
    readonly string _dir = Path.Combine(Path.GetTempPath(), $"bubble-cache-{Guid.NewGuid():N}");

    public void Dispose()
    {
        _relay.Dispose();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public async Task LastLoadedPosts_AreAvailableWithoutTheRelay_AndAreStoredEncrypted()
    {
        var anna = Vault.Create("Anna");
        var ben = Vault.Create("Ben");
        var annas = BubbleMembership.Create("WG", "http://relay/", PostLifetime.Persistent, anna.Identity.PublicKey);
        var bens = Invite.For(annas).ToMembership();
        await _relay.ClientFor(anna).PostAsync(annas, "😊", "Sunrise at the lake", [1, 2, 3]);

        var cache = new FileMessageCache(_dir);
        var online = new BubbleClient(ben, _relay.Relay, _relay.Time, cache);
        Assert.Single((await online.LoadAsync(bens)).Posts);

        // No connection: the relay factory fails, the cache still shows the post.
        var offline = new BubbleClient(ben, _ => throw new HttpRequestException("offline"), _relay.Time, cache);
        var snapshot = await offline.LoadCachedAsync(bens);
        var post = Assert.Single(snapshot!.Posts);
        Assert.Equal("Sunrise at the lake", post.Message.Text);
        Assert.Equal(new byte[] { 1, 2, 3 }, post.Message.Image);

        Assert.DoesNotContain("Sunrise", File.ReadAllText(Directory.GetFiles(_dir).Single()));
    }

    [Fact]
    public async Task CachedPostsStillRespectExpiry_AndDeletedBubblesLeaveNothingBehind()
    {
        var anna = Vault.Create("Anna");
        var annas = BubbleMembership.Create("WG", "http://relay/", PostLifetime.Story24h);
        await _relay.ClientFor(anna).PostAsync(annas, "😊", "gone tomorrow", null);

        var cache = new FileMessageCache(_dir);
        var client = new BubbleClient(anna, _relay.Relay, _relay.Time, cache);
        await client.LoadAsync(annas);
        _relay.Time.Advance(TimeSpan.FromHours(25));
        Assert.Empty((await client.LoadCachedAsync(annas))!.Posts);

        cache.Delete(annas.MailboxId);
        Assert.Null(await client.LoadCachedAsync(annas));
    }
}
