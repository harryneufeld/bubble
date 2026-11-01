using Bubble.Core;

namespace Bubble.Tests;

public class PostLifetimeTests : IDisposable
{
    readonly RelayFixture _relay = new();
    readonly Vault _anna = Vault.Create("Anna");
    readonly Vault _ben = Vault.Create("Ben");

    public void Dispose() => _relay.Dispose();

    /// <summary>Anna owns the bubble, Ben has joined.</summary>
    (BubbleMembership Annas, BubbleMembership Bens) Bubble(PostLifetime max)
    {
        var annas = BubbleMembership.Create("WG", "http://relay/", max, _anna.Identity.PublicKey);
        return (annas, Invite.For(annas).ToMembership());
    }

    [Fact]
    public async Task EachPostPicksItsLifetime_ButNeverLongerThanTheBubbleAllows()
    {
        var (annas, bens) = Bubble(PostLifetime.Persistent);
        await _relay.ClientFor(_anna).PostAsync(annas, "😊", "short", null, PostLifetime.Story24h);
        await _relay.ClientFor(_anna).PostAsync(annas, "😊", "long", null, PostLifetime.Persistent);
        _relay.Time.Advance(TimeSpan.FromHours(25));
        var feed = await _relay.ClientFor(_ben).LoadFeedAsync(bens);
        Assert.Equal("long", Assert.Single(feed).Message.Text);

        var (limited, limitedBens) = Bubble(PostLifetime.Story24h);
        await _relay.ClientFor(_anna).PostAsync(limited, "😊", "wanted permanent", null, PostLifetime.Persistent);
        var post = Assert.Single(await _relay.ClientFor(_ben).LoadFeedAsync(limitedBens));
        Assert.Equal(PostLifetime.Story24h, post.Message.Lifetime);
        Assert.NotNull(post.ExpiresAt);
    }

    [Fact]
    public async Task AuthorMakesPostPermanent_HeartsAndCommentsStayToo()
    {
        var (annas, bens) = Bubble(PostLifetime.Persistent);
        await _relay.ClientFor(_anna).PostAsync(annas, "😊", "keep me", null, PostLifetime.Story24h);
        var benClient = _relay.ClientFor(_ben);
        var post = Assert.Single(await benClient.LoadFeedAsync(bens));
        await benClient.LikeAsync(bens, post, true);
        await benClient.CommentAsync(bens, post, "nice");

        var annaClient = _relay.ClientFor(_anna);
        var annasPost = Assert.Single(await annaClient.LoadFeedAsync(annas));
        await annaClient.SetPostLifetimeAsync(annas, annasPost, PostLifetime.Persistent);

        _relay.Time.Advance(TimeSpan.FromHours(30));
        var snapshot = await benClient.LoadAsync(bens);
        Assert.Equal("keep me", Assert.Single(snapshot.Posts).Message.Text);
        Assert.Single(snapshot.LikersOf(post.Id));
        Assert.Single(snapshot.CommentsOf(post.Id));
    }

    [Fact]
    public async Task AuthorCanLetAPermanentPostDisappear()
    {
        var (annas, bens) = Bubble(PostLifetime.Persistent);
        await _relay.ClientFor(_anna).PostAsync(annas, "😊", "temporary after all", null, PostLifetime.Persistent);
        var annaClient = _relay.ClientFor(_anna);
        await annaClient.SetPostLifetimeAsync(annas, Assert.Single(await annaClient.LoadFeedAsync(annas)), PostLifetime.Story24h);

        Assert.Single(await _relay.ClientFor(_ben).LoadFeedAsync(bens));
        _relay.Time.Advance(TimeSpan.FromHours(25));
        Assert.Empty(await _relay.ClientFor(_ben).LoadFeedAsync(bens));
    }

    [Fact]
    public async Task OnlyTheAuthorCanChangeAPost_AndPermanentNeedsABubbleThatAllowsIt()
    {
        var (annas, bens) = Bubble(PostLifetime.Persistent);
        await _relay.ClientFor(_anna).PostAsync(annas, "😊", "mine", null, PostLifetime.Story24h);
        var benClient = _relay.ClientFor(_ben);
        var post = Assert.Single(await benClient.LoadFeedAsync(bens));
        await Assert.ThrowsAsync<InvalidOperationException>(() => benClient.SetPostLifetimeAsync(bens, post, PostLifetime.Persistent));
        await Assert.ThrowsAsync<InvalidOperationException>(() => benClient.DeletePostAsync(bens, post));

        var wrongSecret = await Assert.ThrowsAsync<HttpRequestException>(() =>
            _relay.Relay("").SetExpiryAsync(bens.MailboxId, post.Id, _ben.Identity.MessageSecret(post.Id), null));
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, wrongSecret.StatusCode);

        var annaClient = _relay.ClientFor(_anna);
        var mine = Assert.Single(await annaClient.LoadFeedAsync(annas));
        annas.Lifetime = PostLifetime.Story24h;
        await Assert.ThrowsAsync<InvalidOperationException>(() => annaClient.SetPostLifetimeAsync(annas, mine, PostLifetime.Persistent));
    }

    [Fact]
    public async Task OnlyTheOwnerSetsTheMaximum_AndLoweringItEndsPermanentPosts()
    {
        var (annas, bens) = Bubble(PostLifetime.Persistent);
        var benClient = _relay.ClientFor(_ben);
        await Assert.ThrowsAsync<InvalidOperationException>(() => benClient.SetLifetimeAsync(bens, PostLifetime.Story24h));

        await _relay.ClientFor(_anna).PostAsync(annas, "😊", "permanent", null, PostLifetime.Persistent);
        await benClient.PostAsync(bens, "😊", "bens permanent", null, PostLifetime.Persistent);
        _relay.Time.Advance(TimeSpan.FromHours(10));
        await _relay.ClientFor(_anna).SetLifetimeAsync(annas, PostLifetime.Story24h);

        // Ben's app notices the new maximum and brings his post on the relay in line.
        Assert.Equal(2, (await benClient.LoadFeedAsync(bens)).Count);
        Assert.Equal(PostLifetime.Story24h, bens.Lifetime);
        var onRelay = await _relay.Relay("").GetMessagesAsync(bens.MailboxId);
        var bensPost = (await benClient.LoadFeedAsync(bens)).Single(p => p.IsMine);
        Assert.NotNull(onRelay.Single(r => r.Id == bensPost.Id).ExpiresAt);

        _relay.Time.Advance(TimeSpan.FromHours(20));
        Assert.Equal(2, (await _relay.ClientFor(_anna).LoadFeedAsync(annas)).Count);
        _relay.Time.Advance(TimeSpan.FromHours(5));
        Assert.Empty(await _relay.ClientFor(_anna).LoadFeedAsync(annas));
    }

    [Fact]
    public async Task MaximumFromANonOwnerIsIgnored()
    {
        var (annas, bens) = Bubble(PostLifetime.Persistent);
        // A message from an older version where any member could still change the setting.
        var forged = MessageCodec.Encrypt(bens, _ben.Identity, new BubbleMessage
        {
            Kind = MessageKind.LifetimeChanged,
            AuthorKey = _ben.Identity.PublicKey,
            AuthorName = "Ben",
            CreatedAt = _relay.Time.GetUtcNow(),
            Lifetime = PostLifetime.Story24h,
        });
        await _relay.Relay("").PostMessageAsync(bens.MailboxId, forged);

        await _relay.ClientFor(_anna).LoadFeedAsync(annas);
        Assert.Equal(PostLifetime.Persistent, annas.Lifetime);
    }

    [Fact]
    public async Task AuthorDeletesPost_HeartsAndCommentsGoWithIt()
    {
        var (annas, bens) = Bubble(PostLifetime.Persistent);
        var annaClient = _relay.ClientFor(_anna);
        await annaClient.PostAsync(annas, "😊", "oops", null, PostLifetime.Persistent);
        var benClient = _relay.ClientFor(_ben);
        var post = Assert.Single(await benClient.LoadFeedAsync(bens));
        await benClient.CommentAsync(bens, post, "hi");

        await annaClient.DeletePostAsync(annas, Assert.Single(await annaClient.LoadFeedAsync(annas)));
        Assert.Empty(await _relay.Relay("").GetMessagesAsync(annas.MailboxId));
    }
}
