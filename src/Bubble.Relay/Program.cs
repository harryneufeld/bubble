using System.Text.RegularExpressions;
using Bubble.Core;
using Bubble.Relay;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = RelayLimits.MaxCiphertextBytes * 4 / 3 + 4096);
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.TypeInfoResolverChain.Insert(0, CoreJson.Default));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(sp => new RelayStore(
    builder.Configuration["Relay:Database"] ?? "relay.db",
    sp.GetRequiredService<TimeProvider>()));
builder.Services.AddHostedService<ExpiryCleanup>();

var app = builder.Build();

app.MapGet("/", () => "Bubble Relay – only stores encrypted data.");

var messages = app.MapGroup("/v1/mailboxes/{mailbox}/messages");

messages.MapPost("/", (string mailbox, RelayMessageIn body, RelayStore store, TimeProvider time) =>
{
    if (!Ids.IsKeyId(mailbox) || !Ids.IsMessageId(body.Id))
        return Results.BadRequest("Invalid ID.");

    var now = time.GetUtcNow();
    if (body.ExpiresAt is { } exp && (exp <= now.ToUnixTimeSeconds() || exp > now.Add(RelayLimits.MaxStoryLifetime).ToUnixTimeSeconds()))
        return Results.BadRequest("Invalid expiry date.");

    if (!TryDecode(body.Ciphertext, RelayLimits.MaxCiphertextBytes, out var ciphertext))
        return Results.BadRequest("Invalid or too large content.");

    return store.AddMessage(mailbox, body.Id, ciphertext, body.ExpiresAt)
        ? Results.Created()
        : Results.Conflict();
});

messages.MapGet("/", (string mailbox, long? after, RelayStore store) =>
    Ids.IsKeyId(mailbox)
        ? Results.Ok(store.GetMessages(mailbox, after ?? 0))
        : Results.BadRequest("Invalid ID."));

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
