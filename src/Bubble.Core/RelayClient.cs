using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Bubble.Core;

/// <summary>HTTP client for the relay. The relay only sees mailbox IDs and encrypted blobs.</summary>
public sealed class RelayClient(HttpClient http)
{
    public async Task PostMessageAsync(string mailboxId, RelayMessageIn message, CancellationToken ct = default)
    {
        using var response = await http.PostAsJsonAsync($"v1/mailboxes/{mailboxId}/messages", message, CoreJson.Default.RelayMessageIn, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<List<RelayMessageOut>> GetMessagesAsync(string mailboxId, long after = 0, CancellationToken ct = default)
    {
        var all = new List<RelayMessageOut>();
        while (true)
        {
            var page = await http.GetFromJsonAsync($"v1/mailboxes/{mailboxId}/messages?after={after}", CoreJson.Default.ListRelayMessageOut, ct) ?? [];
            all.AddRange(page);
            if (page.Count < RelayLimits.PageSize) return all;
            after = page[^1].Seq;
        }
    }

    public async Task PutBackupAsync(BackupState state, byte[] blob, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"v1/backups/{state.BackupId}")
        {
            Content = JsonContent.Create(new BackupBody(Convert.ToBase64String(blob)), CoreJson.Default.BackupBody),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", state.AuthToken);
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>Returns null if there is no backup under these credentials.</summary>
    public async Task<byte[]?> GetBackupAsync(string backupId, string authToken, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"v1/backups/{backupId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authToken);
        using var response = await http.SendAsync(request, ct);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden) return null;
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync(CoreJson.Default.BackupBody, ct);
        return body is null ? null : Convert.FromBase64String(body.Ciphertext);
    }
}

public static class RelayLimits
{
    public const int PageSize = 100;
    public const int MaxCiphertextBytes = 3 * 1024 * 1024;
    public const int MaxBackupBytes = 256 * 1024;
    public static readonly TimeSpan MaxStoryLifetime = TimeSpan.FromHours(25);
}
