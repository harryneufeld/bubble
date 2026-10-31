using System.Net;
using System.Net.Http.Json;
using Bubble.PushGateway;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace Bubble.Tests;

public sealed class FakeWakeSender : IWakeSender
{
    public List<(Platform Platform, string Token)> Sent { get; } = [];

    public WakeResult Result { get; set; } = WakeResult.Sent;

    public Task<WakeResult> SendAsync(Platform platform, string token, CancellationToken ct)
    {
        Sent.Add((platform, token));
        return Task.FromResult(Result);
    }
}

public class GatewayTests : IDisposable
{
    const string Secret = "test-secret-one";

    readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 19, 10, 0, 0, TimeSpan.Zero));
    readonly FakeWakeSender _sender = new();
    readonly WebApplicationFactory<GatewayApp> _factory;

    public GatewayTests()
    {
        _factory = new WebApplicationFactory<GatewayApp>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Push:Secret", Secret);
            b.UseSetting("Push:Fcm:CredentialsFile", "unused-in-tests.json");
            b.ConfigureServices(s =>
            {
                s.Replace(ServiceDescriptor.Singleton<TimeProvider>(_time));
                s.Replace(ServiceDescriptor.Singleton<IWakeSender>(_sender));
            });
        });
    }

    public void Dispose() => _factory.Dispose();

    async Task<string> RegisterAsync(string platform = "fcm", string token = "device-token")
    {
        var response = await _factory.CreateClient().PostAsJsonAsync("v1/register", new RegisterIn(platform, token));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<RegisterOut>())!.Handle;
    }

    Task<HttpResponseMessage> WakeAsync(string handle) =>
        _factory.CreateClient().PostAsync($"v1/wake/{handle}", new StringContent("""{"v":1}"""));

    [Fact]
    public async Task Wake_DeliversToTheRegisteredDevice()
    {
        var handle = await RegisterAsync(token: "device-token");

        var response = await WakeAsync(handle);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal((Platform.Fcm, "device-token"), Assert.Single(_sender.Sent));
    }

    [Fact]
    public async Task Handle_DoesNotRevealTheToken()
    {
        var handle = await RegisterAsync(token: "device-token-plain");

        Assert.DoesNotContain("device-token-plain", handle);
        Assert.DoesNotContain(Convert.ToBase64String("device-token-plain"u8).TrimEnd('='), handle);
    }

    [Fact]
    public async Task RegisteringTwice_GivesDifferentHandlesForTheSameDevice()
    {
        var (a, b) = (await RegisterAsync(), await RegisterAsync());

        Assert.NotEqual(a, b);
        Assert.Equal(HttpStatusCode.Accepted, (await WakeAsync(a)).StatusCode);
    }

    [Fact]
    public async Task WakesInQuickSuccession_AreThrottledPerHandle()
    {
        var handle = await RegisterAsync();
        var other = await RegisterAsync();

        Assert.Equal(HttpStatusCode.Accepted, (await WakeAsync(handle)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await WakeAsync(handle)).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await WakeAsync(other)).StatusCode);

        _time.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(HttpStatusCode.Accepted, (await WakeAsync(handle)).StatusCode);
        Assert.Equal(3, _sender.Sent.Count);
    }

    [Fact]
    public async Task ExpiredHandle_IsGone()
    {
        var handle = await RegisterAsync();

        _time.Advance(TimeSpan.FromDays(91));

        Assert.Equal(HttpStatusCode.Gone, (await WakeAsync(handle)).StatusCode);
        Assert.Empty(_sender.Sent);
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public async Task InvalidHandle_IsGone(string handle)
    {
        var response = await WakeAsync(string.IsNullOrEmpty(handle) ? "x" : handle);

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
    }

    [Fact]
    public async Task TamperedHandle_IsGone()
    {
        var handle = await RegisterAsync();
        var tampered = handle[..^2] + (handle[^2] == 'A' ? "B" : "A") + handle[^1];

        Assert.Equal(HttpStatusCode.Gone, (await WakeAsync(tampered)).StatusCode);
    }

    [Fact]
    public async Task PlatformSaysDeviceIsGone_IsPassedOn()
    {
        var handle = await RegisterAsync();
        _sender.Result = WakeResult.Gone;

        Assert.Equal(HttpStatusCode.Gone, (await WakeAsync(handle)).StatusCode);
    }

    [Fact]
    public async Task PlatformFailure_IsBadGateway()
    {
        var handle = await RegisterAsync();
        _sender.Result = WakeResult.Failed;

        Assert.Equal(HttpStatusCode.BadGateway, (await WakeAsync(handle)).StatusCode);
    }

    [Theory]
    [InlineData("fcm", "")]
    [InlineData("fcm", null)]
    [InlineData("carrier-pigeon", "token")]
    [InlineData(null, "token")]
    public async Task InvalidRegistration_IsRejected(string? platform, string? token)
    {
        var response = await _factory.CreateClient().PostAsJsonAsync("v1/register", new RegisterIn(platform, token));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task OversizedToken_IsRejected()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync(
            "v1/register", new RegisterIn("fcm", new string('t', GatewayOptions.MaxTokenLength + 1)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Apns_IsNotImplementedYet()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync("v1/register", new RegisterIn("apns", "token"));

        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
    }

    [Fact]
    public async Task FcmNotConfigured_RegistrationIsUnavailable()
    {
        using var factory = new WebApplicationFactory<GatewayApp>().WithWebHostBuilder(b => b.UseSetting("Push:Secret", Secret));

        var response = await factory.CreateClient().PostAsJsonAsync("v1/register", new RegisterIn("fcm", "token"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public void HandleCodec_AcceptsPreviousSecretsAfterRotation()
    {
        var old = new HandleCodec(["old-secret"], TimeSpan.FromDays(1), _time);
        var rotated = new HandleCodec(["new-secret", "old-secret"], TimeSpan.FromDays(1), _time);
        var unrelated = new HandleCodec(["new-secret"], TimeSpan.FromDays(1), _time);

        var handle = old.Seal(Platform.Fcm, "token");

        Assert.Equal((Platform.Fcm, "token"), rotated.Open(handle));
        Assert.Null(unrelated.Open(handle));
    }
}
