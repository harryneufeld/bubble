using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Options;

namespace Bubble.Relay;

public sealed class PushOptions
{
    /// <summary>Allow http and private addresses as push endpoints. For local tests only.</summary>
    public bool AllowPrivateEndpoints { get; set; }

    /// <summary>At most one wake per subscription in this time; wakes in between are merged into one.</summary>
    public TimeSpan MinInterval { get; set; } = TimeSpan.FromSeconds(5);
}

/// <summary>
/// Wakes devices when a message arrives in one of their mailboxes. The wake carries no content:
/// the device fetches the messages from the relay itself.
/// </summary>
public sealed class PushDispatcher(
    RelayStore store,
    IHttpClientFactory httpFactory,
    TimeProvider time,
    IOptions<PushOptions> options,
    ILogger<PushDispatcher> logger)
{
    public const string HttpClientName = "push";

    static readonly byte[] WakeBody = Encoding.UTF8.GetBytes("""{"v":1}""");

    readonly ConcurrentDictionary<string, PushTarget> _pending = new();
    readonly ConcurrentDictionary<string, DateTimeOffset> _lastSent = new();

    public void Notify(string mailbox)
    {
        try
        {
            foreach (var target in store.GetSubscriptionsForMailbox(mailbox))
                _pending[target.Id] = target;
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not queue push wakes");
        }
    }

    /// <summary>Sends everything that is due. Called once a second by <see cref="PushWorker"/>.</summary>
    public async Task FlushAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var interval = options.Value.MinInterval;

        foreach (var (id, last) in _lastSent)
            if (now - last >= interval) _lastSent.TryRemove(id, out _);

        var due = _pending.Keys.Where(id => !_lastSent.ContainsKey(id)).ToList();
        await Parallel.ForEachAsync(due, new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = ct }, async (id, token) =>
        {
            if (!_pending.TryRemove(id, out var target)) return;
            _lastSent[id] = now;
            await SendAsync(target, token);
        });
    }

    async Task SendAsync(PushTarget target, CancellationToken ct)
    {
        try
        {
            using var http = httpFactory.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Post, target.Endpoint) { Content = new ByteArrayContent(WakeBody) };
            request.Content.Headers.ContentType = new("application/json");
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
            {
                store.DeleteSubscription(target.Id);
                logger.LogInformation("Push subscription {Id} is gone and was deleted", target.Id);
            }
            else if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Push wake for {Id} failed: {Status}", target.Id, (int)response.StatusCode);
            }
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            // The device catches up the next time it opens the app or the next wake gets through.
            logger.LogWarning("Push wake for {Id} failed: {Message}", target.Id, e.Message);
        }
    }
}

public sealed class PushWorker(PushDispatcher dispatcher) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await dispatcher.FlushAsync(stoppingToken);
    }
}

/// <summary>
/// The relay calls URLs that clients hand it, so it must not become a way into the operator's internal network.
/// </summary>
public static class PushEndpoint
{
    public static bool TryValidate(string endpoint, bool allowPrivate, out Uri uri)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out uri!)) return false;
        if (uri.UserInfo.Length > 0) return false;
        if (uri.Scheme != Uri.UriSchemeHttps && !(allowPrivate && uri.Scheme == Uri.UriSchemeHttp)) return false;
        if (allowPrivate) return true;

        var host = uri.IdnHost;
        if (IPAddress.TryParse(host, out var ip)) return IsPublic(ip);
        return host.Contains('.')
            && !host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
            && !host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
            && !host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase)
            && !host.EndsWith(".lan", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsPublic(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any)) return false;

        var b = ip.GetAddressBytes();
        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            return !(b[0] is 0 or 10 or 127
                || b[0] == 100 && b[1] is >= 64 and <= 127
                || b[0] == 169 && b[1] == 254
                || b[0] == 172 && b[1] is >= 16 and <= 31
                || b[0] == 192 && b[1] == 168
                || b[0] == 192 && b[1] == 0 && b[2] == 0
                || b[0] >= 224);
        }

        return !(ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast || (b[0] & 0xFE) == 0xFC);
    }

    /// <summary>Resolves the host itself and refuses non-public addresses, so a DNS name cannot point into the internal network.</summary>
    public static async ValueTask<Stream> ConnectPublicAsync(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        var addresses = (await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, ct)).Where(IsPublic).ToArray();
        if (addresses.Length == 0) throw new HttpRequestException("The push endpoint does not resolve to a public address.");

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, ct);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
