using System.Text.RegularExpressions;
using Bubble.Core;
using Bubble.Relay;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = RelayLimits.MaxCiphertextBytes * 4 / 3 + 4096);
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.TypeInfoResolverChain.Insert(0, CoreJson.Default));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(sp => new RelayStore(
    builder.Configuration["Relay:Database"] ?? "relay.db",
    sp.GetRequiredService<TimeProvider>()));
builder.Services.AddHostedService<ExpiryCleanup>();
builder.Services.Configure<PushOptions>(builder.Configuration.GetSection("Push"));
builder.Services.AddSingleton<PushDispatcher>();
builder.Services.AddSingleton<PushGatewayClient>();
builder.Services.AddHttpClient(PushDispatcher.GatewayClientName, c => c.Timeout = TimeSpan.FromSeconds(10))
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
builder.Services.AddHostedService<PushWorker>();
builder.Services.AddHttpClient(PushDispatcher.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(10))
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        ConnectCallback = builder.Configuration.GetValue<bool>("Push:AllowPrivateEndpoints") ? null : PushEndpoint.ConnectPublicAsync,
    });

var app = builder.Build();

app.MapGet("/", () => "Bubble Relay – only stores encrypted data.");

var messages = app.MapGroup("/v1/mailboxes/{mailbox}/messages");

messages.MapPost("/", (string mailbox, RelayMessageIn body, RelayStore store, TimeProvider time, PushDispatcher push) =>
{
    if (!Ids.IsKeyId(mailbox) || !Ids.IsMessageId(body.Id))
        return Results.BadRequest("Invalid ID.");

    var now = time.GetUtcNow();
    if (body.ExpiresAt is { } exp && (exp <= now.ToUnixTimeSeconds() || exp > now.Add(RelayLimits.MaxStoryLifetime).ToUnixTimeSeconds()))
        return Results.BadRequest("Invalid expiry date.");

    if (!TryDecode(body.Ciphertext, RelayLimits.MaxCiphertextBytes, out var ciphertext))
        return Results.BadRequest("Invalid or too large content.");

    if (!store.AddMessage(mailbox, body.Id, ciphertext, body.ExpiresAt))
        return Results.Conflict();

    push.Notify(mailbox);
    return Results.Created();
});

messages.MapGet("/", (string mailbox, long? after, RelayStore store) =>
    Ids.IsKeyId(mailbox)
        ? Results.Ok(store.GetMessages(mailbox, after ?? 0))
        : Results.BadRequest("Invalid ID."));

var subscriptions = app.MapGroup("/v1/push/subscriptions");

subscriptions.MapPost("/", async (PushSubscriptionIn body, RelayStore store, IOptions<PushOptions> options, PushGatewayClient gateway, CancellationToken ct) =>
{
    if (!AreMailboxes(body.Mailboxes)) return Results.BadRequest("Invalid push subscription.");

    string endpoint;
    switch (body.Type)
    {
        case PushTypes.UnifiedPush:
            if (body.Endpoint is not { Length: > 0 and <= RelayLimits.MaxPushEndpointLength } url
                || body.Platform is not null || body.Token is not null
                || !PushEndpoint.TryValidate(url, options.Value.AllowPrivateEndpoints, out _))
                return Results.BadRequest("Invalid push subscription.");
            endpoint = url;
            break;

        case PushTypes.Gateway:
            if (!gateway.IsConfigured) return Results.StatusCode(StatusCodes.Status501NotImplemented);
            if (body.Endpoint is not null || !IsGatewayRegistration(body.Platform, body.Token))
                return Results.BadRequest("Invalid push subscription.");
            if (await gateway.RegisterAsync(body.Platform!, body.Token!, ct) is not { } wakeUrl)
                return Results.StatusCode(StatusCodes.Status502BadGateway);
            endpoint = wakeUrl;
            break;

        default:
            return Results.BadRequest("Invalid push subscription.");
    }

    var created = store.CreateSubscription(body.Type, endpoint, body.Mailboxes);
    return Results.Created($"/v1/push/subscriptions/{created.Id}", created);
});

subscriptions.MapPut("/{id}", async (string id, PushMailboxesBody body, HttpRequest request, RelayStore store, PushGatewayClient gateway, CancellationToken ct) =>
{
    if (!Ids.IsMessageId(id) || BearerToken(request) is not { } secret || !AreMailboxes(body.Mailboxes))
        return Results.BadRequest();
    if (store.GetSubscriptionType(id, secret) is not { } type)
        return Results.NotFound();

    // Renew the handle before it expires: the token is only passed through to the gateway, never stored.
    string? newEndpoint = null;
    if (body.Platform is not null || body.Token is not null)
    {
        if (type != PushTypes.Gateway || !IsGatewayRegistration(body.Platform, body.Token))
            return Results.BadRequest();
        newEndpoint = await gateway.RegisterAsync(body.Platform!, body.Token!, ct);
        if (newEndpoint is null) return Results.StatusCode(StatusCodes.Status502BadGateway);
    }

    return store.UpdateSubscriptionMailboxes(id, secret, body.Mailboxes, newEndpoint) ? Results.NoContent() : Results.NotFound();
});

subscriptions.MapDelete("/{id}", (string id, HttpRequest request, RelayStore store) =>
{
    if (!Ids.IsMessageId(id) || BearerToken(request) is not { } secret)
        return Results.BadRequest();
    return store.DeleteSubscription(id, secret) ? Results.NoContent() : Results.NotFound();
});

app.MapPut("/v1/backups/{backupId}", (string backupId, BackupBody body, HttpRequest request, RelayStore store) =>
{
    if (!Ids.IsKeyId(backupId) || BearerToken(request) is not { } token)
        return Results.BadRequest();
    if (!TryDecode(body.Ciphertext, RelayLimits.MaxBackupBytes, out var ciphertext))
        return Results.BadRequest("Invalid or too large content.");

    return store.PutBackup(backupId, token, ciphertext) switch
    {
        RelayStore.BackupWrite.Created => Results.Created(),
        RelayStore.BackupWrite.Updated => Results.NoContent(),
        _ => Results.StatusCode(StatusCodes.Status403Forbidden),
    };
});

app.MapGet("/v1/backups/{backupId}", (string backupId, HttpRequest request, RelayStore store) =>
{
    if (!Ids.IsKeyId(backupId) || BearerToken(request) is not { } token)
        return Results.BadRequest();
    return store.GetBackup(backupId, token) is { } ciphertext
        ? Results.Ok(new BackupBody(Convert.ToBase64String(ciphertext)))
        : Results.NotFound();
});

app.Run();

static bool TryDecode(string base64, int maxBytes, out byte[] bytes)
{
    bytes = [];
    if (base64.Length > maxBytes * 4 / 3 + 4) return false;
    try { bytes = Convert.FromBase64String(base64); }
    catch (FormatException) { return false; }
    return bytes.Length is > 0 && bytes.Length <= maxBytes;
}

static bool IsGatewayRegistration(string? platform, string? token) =>
    platform is "fcm" or "apns" && token is { Length: > 0 and <= 4096 };

static bool AreMailboxes(List<string> mailboxes) =>
    mailboxes.Count <= RelayLimits.MaxPushMailboxes && mailboxes.All(Ids.IsKeyId);

static string? BearerToken(HttpRequest request)
{
    var header = request.Headers.Authorization.ToString();
    return header.StartsWith("Bearer ", StringComparison.Ordinal) && Ids.IsKeyId(header[7..]) ? header[7..] : null;
}

static partial class Ids
{
    /// <summary>Mailbox and backup IDs and tokens: 32 bytes as Base64Url.</summary>
    public static bool IsKeyId(string s) => KeyId().IsMatch(s);

    public static bool IsMessageId(string s) => MessageId().IsMatch(s);

    [GeneratedRegex("^[A-Za-z0-9_-]{43}$")] private static partial Regex KeyId();
    [GeneratedRegex("^[A-Za-z0-9_-]{16,64}$")] private static partial Regex MessageId();
}

public partial class Program;
