using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Bubble.PushGateway;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<GatewayOptions>(builder.Configuration.GetSection("Push"));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(sp =>
{
    var options = sp.GetRequiredService<IOptions<GatewayOptions>>().Value;
    if (string.IsNullOrWhiteSpace(options.Secret))
        throw new InvalidOperationException("Push:Secret is not set. Generate one with: openssl rand -base64 32");
    return new HandleCodec(
        [options.Secret, .. options.PreviousSecrets.Where(s => !string.IsNullOrWhiteSpace(s))],
        TimeSpan.FromDays(options.HandleLifetimeDays),
        sp.GetRequiredService<TimeProvider>());
});
builder.Services.AddSingleton<WakeThrottle>();
builder.Services.AddHttpClient<FcmHttp>();
builder.Services.AddSingleton<IWakeSender>(sp =>
{
    var options = sp.GetRequiredService<IOptions<GatewayOptions>>().Value;
    return string.IsNullOrWhiteSpace(options.Fcm.CredentialsFile)
        ? new NoSender()
        : new FcmSender(options.Fcm.CredentialsFile, sp.GetRequiredService<FcmHttp>().Client, sp.GetRequiredService<ILogger<FcmSender>>());
});

// Per client address. Behind a reverse proxy, set ASPNETCORE_FORWARDEDHEADERS_ENABLED=true so this is the caller and not the proxy.
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("register", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1) }));
    o.AddPolicy("wake", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 1200, Window = TimeSpan.FromMinutes(1) }));
});

var app = builder.Build();
app.UseRateLimiter();

app.MapGet("/", () => "Bubble Push Gateway – only wakes devices, never sees content.");
app.MapGet("/healthz", () => Results.Ok());

// The app (through its relay) hands over its FCM/APNs token and gets an opaque handle back.
app.MapPost("/v1/register", (RegisterIn body, HandleCodec codec, IOptions<GatewayOptions> options) =>
{
    if (body.Token is not { Length: > 0 and <= GatewayOptions.MaxTokenLength } token)
        return Results.BadRequest("Invalid token.");

    switch (body.Platform)
    {
        case "fcm":
            if (string.IsNullOrWhiteSpace(options.Value.Fcm.CredentialsFile)) return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            return Results.Ok(new RegisterOut(codec.Seal(Platform.Fcm, token)));
        case "apns":
            return Results.StatusCode(StatusCodes.Status501NotImplemented);
        default:
            return Results.BadRequest("Unknown platform.");
    }
}).RequireRateLimiting("register");

// A relay presents the handle when something new arrived. The body is ignored; the wake carries no content.
app.MapPost("/v1/wake/{handle}", async (string handle, HandleCodec codec, IWakeSender sender, WakeThrottle throttle, CancellationToken ct) =>
{
    if (codec.Open(handle) is not var (platform, token))
        return Results.StatusCode(StatusCodes.Status410Gone);
    if (!throttle.TryAcquire(handle))
        return Results.StatusCode(StatusCodes.Status429TooManyRequests);

    return await sender.SendAsync(platform, token, ct) switch
    {
        WakeResult.Sent => Results.Accepted(),
        WakeResult.Gone => Results.StatusCode(StatusCodes.Status410Gone),
        _ => Results.StatusCode(StatusCodes.Status502BadGateway),
    };
}).RequireRateLimiting("wake");

app.Run();

namespace Bubble.PushGateway
{
    public sealed record RegisterIn(string? Platform, string? Token);

    public sealed record RegisterOut(string Handle);

    public sealed class GatewayOptions
    {
        public const int MaxTokenLength = 4096;

        /// <summary>Seals the handles. 32 random bytes as Base64 are plenty.</summary>
        public string Secret { get; set; } = "";

        /// <summary>Older secrets that are still accepted, so handles survive a key change.</summary>
        public List<string> PreviousSecrets { get; set; } = [];

        /// <summary>The app renews its handle regularly, so this only has to outlast a long pause.</summary>
        public int HandleLifetimeDays { get; set; } = 90;

        /// <summary>At most one wake per handle in this time.</summary>
        public TimeSpan MinWakeInterval { get; set; } = TimeSpan.FromSeconds(10);

        public FcmOptions Fcm { get; set; } = new();
    }

    public sealed class FcmOptions
    {
        /// <summary>Firebase service account key (JSON). Without it, FCM is switched off.</summary>
        public string? CredentialsFile { get; set; }
    }

    /// <summary>Typed holder so the FCM sender gets a client from the factory.</summary>
    public sealed class FcmHttp(HttpClient client)
    {
        public HttpClient Client { get; } = client;
    }

    /// <summary>Keeps a leaked handle from being used to flood a device. Remembers only a hash of the handle.</summary>
    public sealed class WakeThrottle(TimeProvider time, IOptions<GatewayOptions> options)
    {
        readonly ConcurrentDictionary<string, DateTimeOffset> _last = new();
        DateTimeOffset _lastCleanup;

        public bool TryAcquire(string handle)
        {
            var now = time.GetUtcNow();
            var interval = options.Value.MinWakeInterval;
            Cleanup(now, interval);

            var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(handle)), 0, 8);
            var allowed = false;
            _last.AddOrUpdate(key,
                _ => { allowed = true; return now; },
                (_, last) =>
                {
                    allowed = now - last >= interval;
                    return allowed ? now : last;
                });
            return allowed;
        }

        void Cleanup(DateTimeOffset now, TimeSpan interval)
        {
            if (now - _lastCleanup < interval) return;
            _lastCleanup = now;
            foreach (var (key, last) in _last)
                if (now - last >= interval) _last.TryRemove(key, out _);
        }
    }

    /// <summary>Used when FCM isn't configured: registering already answers 503, this covers handles from an earlier setup.</summary>
    public sealed class NoSender : IWakeSender
    {
        public Task<WakeResult> SendAsync(Platform platform, string token, CancellationToken ct) => Task.FromResult(WakeResult.Failed);
    }

    /// <summary>Entry point marker for tests.</summary>
    public sealed class GatewayApp;
}
