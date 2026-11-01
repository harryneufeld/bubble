using System.Security.Cryptography;
using Bubble.Core;
using Bubble.Relay;

namespace Bubble.Tests;

public sealed class MemoryPushStateStore : IPushStateStore
{
    public string? Value { get; private set; }

    public Task<string?> GetAsync() => Task.FromResult(Value);

    public Task SetAsync(string value)
    {
        Value = value;
        return Task.CompletedTask;
    }

    public Task ClearAsync()
    {
        Value = null;
        return Task.CompletedTask;
    }
}

public sealed class FakePushProvider(string? token = "token-1") : IPushProvider
{
    public string? Token { get; set; } = token;

    public string Platform => "fcm";

    public Task<string?> GetTokenAsync(CancellationToken ct = default) => Task.FromResult(Token);
}

public class PushRegistrarTests : IDisposable
{
    readonly RelayFixture _relay = new();
    readonly MemoryPushStateStore _store = new();
    readonly FakePushProvider _provider = new();
    readonly PushRegistrar _registrar;

    public PushRegistrarTests()
    {
        _registrar = new PushRegistrar(_ => new RelayClient(_relay.Http()), _store, _relay.Time);
    }

    public void Dispose() => _relay.Dispose();

    static BubbleMembership NewBubble(string relayUrl = "http://relay-a/") =>
        BubbleMembership.Create("WG", relayUrl, PostLifetime.Story24h);

    int Registrations => _relay.Push.Requests.Count(r => r.Uri.AbsolutePath == "/v1/register");

    async Task PostToAsync(BubbleMembership bubble)
    {
        var id = Base64Url.Encode(RandomNumberGenerator.GetBytes(16));
        await new RelayClient(_relay.Http()).PostMessageAsync(bubble.MailboxId, new RelayMessageIn(id, Convert.ToBase64String([1, 2, 3]), null));
        await _relay.Service<PushDispatcher>().FlushAsync(CancellationToken.None);
    }

    int Wakes => _relay.Push.Requests.Count(r => r.Uri.AbsolutePath.StartsWith("/v1/wake/"));

    [Fact]
    public async Task FirstSync_RegistersAndWakesOnNewMessages()
    {
        var bubble = NewBubble();

        Assert.True(await _registrar.SyncAsync(_provider, [bubble]));
        await PostToAsync(bubble);

        Assert.Equal(1, Registrations);
        Assert.Equal(1, Wakes);
    }

    [Fact]
    public async Task UnchangedState_DoesNotTalkToTheRelayAgain()
    {
        var bubble = NewBubble();
        await _registrar.SyncAsync(_provider, [bubble]);
        var before = _relay.Push.Requests.Count;

        Assert.True(await _registrar.SyncAsync(_provider, [bubble]));

        Assert.Equal(before, _relay.Push.Requests.Count);
    }

    [Fact]
    public async Task JoiningAnotherBubble_WakesForItToo()
    {
        var first = NewBubble();
        var second = NewBubble();
        await _registrar.SyncAsync(_provider, [first]);

        await _registrar.SyncAsync(_provider, [first, second]);
        await PostToAsync(second);

        Assert.Equal(1, Wakes);
    }

    [Fact]
    public async Task LeavingAllBubblesOfARelay_RemovesTheSubscription()
    {
        var bubble = NewBubble();
        await _registrar.SyncAsync(_provider, [bubble]);

        await _registrar.SyncAsync(_provider, []);
        await PostToAsync(bubble);

        Assert.Equal(0, Wakes);
        Assert.Null(_store.Value);
    }

    [Fact]
    public async Task PushSwitchedOff_RemovesEverythingAndForgetsState()
    {
        var bubble = NewBubble();
        await _registrar.SyncAsync(_provider, [bubble]);

        await _registrar.SyncAsync(null, [bubble]);
        await PostToAsync(bubble);

        Assert.Equal(0, Wakes);
        Assert.Null(_store.Value);
    }

    [Fact]
    public async Task NoToken_RegistersNothing()
    {
        Assert.True(await _registrar.SyncAsync(new FakePushProvider(null), [NewBubble()]));

        Assert.Equal(0, Registrations);
    }

    [Fact]
    public async Task NewToken_GetsANewHandle()
    {
        var bubble = NewBubble();
        await _registrar.SyncAsync(_provider, [bubble]);

        _provider.Token = "token-2";
        await _registrar.SyncAsync(_provider, [bubble]);
        await PostToAsync(bubble);

        Assert.Equal(2, Registrations);
        Assert.EndsWith("/v1/wake/handle-2", _relay.Push.Requests[^1].Uri.AbsolutePath);
    }

    [Fact]
    public async Task HandleIsRenewedAfterTwoWeeks_NotBefore()
    {
        var bubble = NewBubble();
        await _registrar.SyncAsync(_provider, [bubble]);

        _relay.Time.Advance(PushRegistrar.RenewAfter - TimeSpan.FromHours(1));
        await _registrar.SyncAsync(_provider, [bubble]);
        Assert.Equal(1, Registrations);

        _relay.Time.Advance(TimeSpan.FromHours(2));
        await _registrar.SyncAsync(_provider, [bubble]);
        Assert.Equal(2, Registrations);
    }

    [Fact]
    public async Task RelayThatForgotTheSubscription_GetsANewOne()
    {
        var bubble = NewBubble();
        await _registrar.SyncAsync(_provider, [bubble]);

        _relay.Time.Advance(RelayLimits.PushSubscriptionTtl + TimeSpan.FromDays(2));
        _relay.Service<RelayStore>().DeleteStaleSubscriptions();
        await _registrar.SyncAsync(_provider, [bubble]);
        await PostToAsync(bubble);

        Assert.Equal(1, Wakes);
    }

    [Fact]
    public async Task GatewayDown_IsReportedAndRetriedNextTime()
    {
        var bubble = NewBubble();
        _relay.Push.RegisterWorks = false;

        Assert.False(await _registrar.SyncAsync(_provider, [bubble]));
        Assert.Null(_store.Value);

        _relay.Push.RegisterWorks = true;
        Assert.True(await _registrar.SyncAsync(_provider, [bubble]));
        await PostToAsync(bubble);
        Assert.Equal(1, Wakes);
    }

    [Fact]
    public async Task EachRelayGetsItsOwnSubscription_WithOnlyItsOwnBubbles()
    {
        var a = NewBubble("http://relay-a/");
        var b = NewBubble("http://relay-b/");
        await _registrar.SyncAsync(_provider, [a, b]);
        Assert.Equal(2, Registrations);

        await _registrar.SyncAsync(_provider, [a]); // b's relay no longer has bubbles of mine
        await PostToAsync(b);
        Assert.Equal(0, Wakes);
        await PostToAsync(a);
        Assert.Equal(1, Wakes);
    }

    [Fact]
    public async Task StateInTheStore_DoesNotContainTheToken()
    {
        await _registrar.SyncAsync(new FakePushProvider("secret-device-token"), [NewBubble()]);

        Assert.DoesNotContain("secret-device-token", _store.Value);
    }

    [Fact]
    public void PostNotifications_AnnounceOnlyNewPostsByOthers()
    {
        BubbleMessage Message(string author, string? text, DateTimeOffset at) => new()
        {
            Kind = MessageKind.Post, AuthorKey = author, AuthorName = author, CreatedAt = at,
            Lifetime = PostLifetime.Story24h, Emoji = "🥳", Text = text,
        };
        var now = DateTimeOffset.UtcNow;
        var snapshot = new BubbleSnapshot(
            [
                new FeedPost("new-2", Message("Ben", "Second\nline", now), false),
                new FeedPost("new-1", Message("Anna", "First", now.AddMinutes(-5)), false),
                new FeedPost("seen", Message("Anna", "Old", now.AddHours(-1)), false),
                new FeedPost("mine", Message("Me", "Mine", now), true),
            ],
            [], "me", [], []);

        var fresh = PostNotifications.NewPosts(snapshot, new HashSet<string> { "seen" });

        Assert.Equal(["new-1", "new-2"], fresh.Select(p => p.Id));
        Assert.Equal("Anna 🥳 First", PostNotifications.Describe(fresh[0]));
        Assert.Equal("Ben 🥳 Second line", PostNotifications.Describe(fresh[1]));
    }

    [Fact]
    public void PostNotifications_ShortenLongTexts()
    {
        var post = new FeedPost("x", new BubbleMessage
        {
            Kind = MessageKind.Post, AuthorKey = "a", AuthorName = "Anna", CreatedAt = DateTimeOffset.UtcNow,
            Lifetime = PostLifetime.Story24h, Text = new string('w', 500),
        }, false);

        var line = PostNotifications.Describe(post);

        Assert.True(line.Length <= 100);
        Assert.EndsWith("…", line);
    }
}
