using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Bubble.Core;
using Bubble.Relay;

namespace Bubble.Tests;

/// <summary>Stands in for the push gateway or UnifiedPush server: records every wake and answers with a chosen status.</summary>
public sealed class FakePushEndpoint : HttpMessageHandler
{
    readonly List<(Uri Uri, string Body)> _requests = [];

    public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

    public IReadOnlyList<(Uri Uri, string Body)> Requests
    {
        get { lock (_requests) return [.. _requests]; }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
        lock (_requests) _requests.Add((request.RequestUri!, body));
        return new HttpResponseMessage(Status);
    }
}

public class PushTests : IDisposable
{
    const string Endpoint = "https://push.example.org/v1/wake/handle-1";

    readonly RelayFixture _relay = new();

    public void Dispose() => _relay.Dispose();

    static string NewMailbox() => Base64Url.Encode(RandomNumberGenerator.GetBytes(32));

    static RelayMessageIn NewMessage() =>
        new(Base64Url.Encode(RandomNumberGenerator.GetBytes(16)), Convert.ToBase64String([1, 2, 3]), null);

    async Task FlushAsync() => await _relay.Service<PushDispatcher>().FlushAsync(CancellationToken.None);

    [Fact]
    public async Task NewMessage_WakesSubscribedDevice_WithoutAnyContent()
    {
        var relay = new RelayClient(_relay.Http());
        var mailbox = NewMailbox();
        await relay.CreatePushSubscriptionAsync(new(PushTypes.Gateway, Endpoint, [mailbox]));

        await relay.PostMessageAsync(mailbox, NewMessage());
        await FlushAsync();

        var request = Assert.Single(_relay.Push.Requests);
        Assert.Equal(new Uri(Endpoint), request.Uri);
        Assert.Equal("""{"v":1}""", request.Body);
    }

    [Fact]
    public async Task MessageInOtherMailbox_DoesNotWake()
    {
        var relay = new RelayClient(_relay.Http());
        await relay.CreatePushSubscriptionAsync(new(PushTypes.UnifiedPush, Endpoint, [NewMailbox()]));

        await relay.PostMessageAsync(NewMailbox(), NewMessage());
        await FlushAsync();

        Assert.Empty(_relay.Push.Requests);
    }

    [Fact]
    public async Task RapidMessages_AreMergedIntoOneWake_AndNextOneWaitsForTheInterval()
    {
        var relay = new RelayClient(_relay.Http());
        var mailbox = NewMailbox();
        await relay.CreatePushSubscriptionAsync(new(PushTypes.Gateway, Endpoint, [mailbox]));

        await relay.PostMessageAsync(mailbox, NewMessage());
        await relay.PostMessageAsync(mailbox, NewMessage());
        await FlushAsync();
        Assert.Single(_relay.Push.Requests);

        await relay.PostMessageAsync(mailbox, NewMessage());
        await FlushAsync();
        Assert.Single(_relay.Push.Requests);

        _relay.Time.Advance(TimeSpan.FromSeconds(5));
        await FlushAsync();
        Assert.Equal(2, _relay.Push.Requests.Count);
    }

    [Fact]
    public async Task Subscription_FollowsItsMailboxList()
    {
        var relay = new RelayClient(_relay.Http());
        var (first, second) = (NewMailbox(), NewMailbox());
        var subscription = await relay.CreatePushSubscriptionAsync(new(PushTypes.Gateway, Endpoint, [first]));

        Assert.True(await relay.UpdatePushMailboxesAsync(subscription, [second]));

        await relay.PostMessageAsync(first, NewMessage());
        await FlushAsync();
        Assert.Empty(_relay.Push.Requests);

        await relay.PostMessageAsync(second, NewMessage());
        await FlushAsync();
        Assert.Single(_relay.Push.Requests);
    }

    [Fact]
    public async Task WrongSecret_CannotChangeOrDeleteSubscription()
    {
        var relay = new RelayClient(_relay.Http());
        var mailbox = NewMailbox();
        var subscription = await relay.CreatePushSubscriptionAsync(new(PushTypes.Gateway, Endpoint, [mailbox]));
        var wrong = subscription with { Secret = Base64Url.Encode(RandomNumberGenerator.GetBytes(32)) };

        Assert.False(await relay.UpdatePushMailboxesAsync(wrong, []));
        await relay.DeletePushSubscriptionAsync(wrong);

        await relay.PostMessageAsync(mailbox, NewMessage());
        await FlushAsync();
        Assert.Single(_relay.Push.Requests);
    }

    [Fact]
    public async Task DeletedSubscription_IsNotWoken()
    {
        var relay = new RelayClient(_relay.Http());
        var mailbox = NewMailbox();
        var subscription = await relay.CreatePushSubscriptionAsync(new(PushTypes.Gateway, Endpoint, [mailbox]));

        await relay.DeletePushSubscriptionAsync(subscription);
        await relay.PostMessageAsync(mailbox, NewMessage());
        await FlushAsync();

        Assert.Empty(_relay.Push.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Gone)]
    public async Task EndpointReportingDeviceGone_DeletesSubscription(HttpStatusCode status)
    {
        var relay = new RelayClient(_relay.Http());
        var mailbox = NewMailbox();
        var subscription = await relay.CreatePushSubscriptionAsync(new(PushTypes.Gateway, Endpoint, [mailbox]));

        _relay.Push.Status = status;
        await relay.PostMessageAsync(mailbox, NewMessage());
        await FlushAsync();

        Assert.False(await relay.UpdatePushMailboxesAsync(subscription, [mailbox]));
    }

    [Fact]
    public async Task EndpointServerError_KeepsSubscription_AndDoesNotBreakPosting()
    {
        var relay = new RelayClient(_relay.Http());
        var mailbox = NewMailbox();
        var subscription = await relay.CreatePushSubscriptionAsync(new(PushTypes.Gateway, Endpoint, [mailbox]));

        _relay.Push.Status = HttpStatusCode.InternalServerError;
        await relay.PostMessageAsync(mailbox, NewMessage());
        await FlushAsync();

        Assert.True(await relay.UpdatePushMailboxesAsync(subscription, [mailbox]));
    }

    [Fact]
    public async Task SubscriptionNotRenewedFor60Days_IsDeleted()
    {
        var relay = new RelayClient(_relay.Http());
        var mailbox = NewMailbox();
        var subscription = await relay.CreatePushSubscriptionAsync(new(PushTypes.Gateway, Endpoint, [mailbox]));

        _relay.Time.Advance(TimeSpan.FromDays(59));
        Assert.Equal(0, _relay.Service<RelayStore>().DeleteStaleSubscriptions());
        Assert.True(await relay.UpdatePushMailboxesAsync(subscription, [mailbox])); // renews

        _relay.Time.Advance(TimeSpan.FromDays(59));
        Assert.Equal(0, _relay.Service<RelayStore>().DeleteStaleSubscriptions());

        _relay.Time.Advance(TimeSpan.FromDays(2));
        Assert.Equal(1, _relay.Service<RelayStore>().DeleteStaleSubscriptions());
        Assert.False(await relay.UpdatePushMailboxesAsync(subscription, [mailbox]));
    }

    [Theory]
    [InlineData("http://push.example.org/wake")] // no TLS
    [InlineData("https://localhost/wake")]
    [InlineData("https://intranet/wake")] // single-label host
    [InlineData("https://nas.local/wake")]
    [InlineData("https://127.0.0.1/wake")]
    [InlineData("https://10.0.0.5/wake")]
    [InlineData("https://192.168.1.10/wake")]
    [InlineData("https://169.254.169.254/latest/meta-data")]
    [InlineData("https://[::1]/wake")]
    [InlineData("https://[::ffff:10.0.0.5]/wake")]
    [InlineData("https://user:secret@push.example.org/wake")]
    [InlineData("ftp://push.example.org/wake")]
    [InlineData("not a url")]
    public async Task EndpointsPointingNowhereUseful_AreRejected(string endpoint)
    {
        var response = await _relay.Http().PostAsJsonAsync(
            "v1/push/subscriptions", new PushSubscriptionIn(PushTypes.Gateway, endpoint, [NewMailbox()]), CoreJson.Default.PushSubscriptionIn);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task InvalidTypeOrMailboxes_AreRejected()
    {
        var http = _relay.Http();
        var bad = new PushSubscriptionIn[]
        {
            new("carrier-pigeon", Endpoint, [NewMailbox()]),
            new(PushTypes.Gateway, Endpoint, ["not-a-mailbox"]),
            new(PushTypes.Gateway, Endpoint, [.. Enumerable.Range(0, RelayLimits.MaxPushMailboxes + 1).Select(_ => NewMailbox())]),
            new(PushTypes.Gateway, "https://push.example.org/" + new string('a', RelayLimits.MaxPushEndpointLength), [NewMailbox()]),
        };

        foreach (var subscription in bad)
        {
            var response = await http.PostAsJsonAsync("v1/push/subscriptions", subscription, CoreJson.Default.PushSubscriptionIn);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }

    [Fact]
    public async Task UpdateWithoutBearerToken_IsRejected()
    {
        var relay = new RelayClient(_relay.Http());
        var subscription = await relay.CreatePushSubscriptionAsync(new(PushTypes.Gateway, Endpoint, [NewMailbox()]));

        using var request = new HttpRequestMessage(HttpMethod.Put, $"v1/push/subscriptions/{subscription.Id}")
        {
            Content = JsonContent.Create(new PushMailboxesBody([]), CoreJson.Default.PushMailboxesBody),
        };
        var response = await _relay.Http().SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("8.8.8.8", true)]
    [InlineData("2606:4700:4700::1111", true)]
    [InlineData("127.0.0.1", false)]
    [InlineData("10.1.2.3", false)]
    [InlineData("172.16.0.1", false)]
    [InlineData("172.32.0.1", true)]
    [InlineData("192.168.0.1", false)]
    [InlineData("100.64.0.1", false)]
    [InlineData("169.254.1.1", false)]
    [InlineData("0.0.0.0", false)]
    [InlineData("224.0.0.1", false)]
    [InlineData("::1", false)]
    [InlineData("fe80::1", false)]
    [InlineData("fd00::1", false)]
    [InlineData("::ffff:192.168.0.1", false)]
    [InlineData("::ffff:8.8.8.8", true)]
    public void IsPublic_ClassifiesAddresses(string address, bool expected) =>
        Assert.Equal(expected, PushEndpoint.IsPublic(IPAddress.Parse(address)));
}
