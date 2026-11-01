using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Google.Apis.Auth.OAuth2;

namespace Bubble.PushGateway;

public enum WakeResult
{
    Sent,

    /// <summary>The platform says the token is no longer valid (app uninstalled, token rotated).</summary>
    Gone,

    Failed,
}

public interface IWakeSender
{
    Task<WakeResult> SendAsync(Platform platform, string token, CancellationToken ct);
}

/// <summary>Sends content-free data messages through the FCM HTTP v1 API.</summary>
public sealed class FcmSender : IWakeSender
{
    const string Scope = "https://www.googleapis.com/auth/firebase.messaging";
    static readonly byte[] Payload = Encoding.UTF8.GetBytes("""{"v":"1"}""");

    readonly GoogleCredential _credential;
    readonly string _sendUrl;
    readonly HttpClient _http;
    readonly ILogger<FcmSender> _logger;

    public FcmSender(string credentialsFile, HttpClient http, ILogger<FcmSender> logger)
    {
        var json = File.ReadAllText(credentialsFile);
        using var doc = JsonDocument.Parse(json);
        var projectId = doc.RootElement.GetProperty("project_id").GetString()
            ?? throw new InvalidOperationException("The FCM credentials file has no project_id.");
        _credential = CredentialFactory.FromJson<ServiceAccountCredential>(json).ToGoogleCredential().CreateScoped(Scope);
        _sendUrl = $"https://fcm.googleapis.com/v1/projects/{projectId}/messages:send";
        _http = http;
        _logger = logger;
    }

    public async Task<WakeResult> SendAsync(Platform platform, string token, CancellationToken ct)
    {
        if (platform != Platform.Fcm) return WakeResult.Failed;

        try
        {
            var accessToken = await _credential.UnderlyingCredential.GetAccessTokenForRequestAsync(cancellationToken: ct);
            var message = new
            {
                message = new
                {
                    token,
                    data = new { v = "1" },
                    android = new { priority = "HIGH" },
                },
            };
            using var request = new HttpRequestMessage(HttpMethod.Post, _sendUrl) { Content = JsonContent(message) };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await _http.SendAsync(request, ct);

            if (response.IsSuccessStatusCode) return WakeResult.Sent;
            if (response.StatusCode == HttpStatusCode.NotFound) return WakeResult.Gone; // UNREGISTERED
            if (response.StatusCode == HttpStatusCode.BadRequest && (await response.Content.ReadAsStringAsync(ct)).Contains("INVALID_ARGUMENT"))
                return WakeResult.Gone; // not a valid token at all

            _logger.LogWarning("FCM answered {Status}", (int)response.StatusCode);
            return WakeResult.Failed;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or Google.GoogleApiException or InvalidOperationException && !ct.IsCancellationRequested)
        {
            _logger.LogWarning("FCM request failed: {Message}", e.Message);
            return WakeResult.Failed;
        }
    }

    static ByteArrayContent JsonContent(object value)
    {
        var content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(value));
        content.Headers.ContentType = new("application/json");
        return content;
    }
}
