using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Bubble.Core;

/// <summary>Where a device gets its push token from: FCM on Android, APNs on iOS.</summary>
public interface IPushProvider
{
    /// <summary>The platform name the push gateway knows: <c>fcm</c> or <c>apns</c>.</summary>
    string Platform { get; }

    /// <summary>The current device token, or null if push is not available (not set up in this build, no Google services, no permission).</summary>
    Task<string?> GetTokenAsync(CancellationToken ct = default);
}

/// <summary>Keeps the registrar's state between runs. The state holds subscription secrets, so it belongs in secure storage.</summary>
public interface IPushStateStore
{
    Task<string?> GetAsync();

    Task SetAsync(string value);

    Task ClearAsync();
}

/// <summary>What the device knows about its subscription at one relay.</summary>
public sealed record PushRelayState(string Id, string Secret, string Fingerprint, DateTimeOffset RenewedAt);

/// <summary>
/// Keeps one push subscription per relay in line with the bubbles on that relay. Idempotent: call it whenever bubbles
/// change, the token changes or the app starts. It only talks to a relay when something changed or the handle is due for renewal.
/// </summary>
public sealed class PushRegistrar(Func<string, RelayClient> relayFor, IPushStateStore store, TimeProvider? time = null)
{
    /// <summary>The gateway's handles last 90 days; renewing every two weeks leaves plenty of room.</summary>
    public static readonly TimeSpan RenewAfter = TimeSpan.FromDays(14);

    readonly TimeProvider _time = time ?? TimeProvider.System;
    readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Brings the relays in line with <paramref name="bubbles"/>. With no <paramref name="provider"/> (push off or unavailable)
    /// all subscriptions are removed. False if a relay could not be reached; the next call tries again.
    /// </summary>
    public async Task<bool> SyncAsync(IPushProvider? provider, IEnumerable<BubbleMembership> bubbles, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var state = await LoadAsync();
            var token = provider is null ? null : await provider.GetTokenAsync(ct);
            var wanted = token is null
                ? new Dictionary<string, List<string>>()
                : bubbles.GroupBy(b => b.RelayUrl).ToDictionary(g => g.Key, g => g.Select(b => b.MailboxId).Distinct().Order().Take(RelayLimits.MaxPushMailboxes).ToList());

            var allOk = true;
            foreach (var (url, old) in state.Where(s => !wanted.ContainsKey(s.Key)).ToList())
            {
                // Best effort: if the relay is gone, its own cleanup removes the subscription after 60 days.
                try { await relayFor(url).DeletePushSubscriptionAsync(new PushSubscriptionOut(old.Id, old.Secret), ct); }
                catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested) { }
                state.Remove(url);
            }

            foreach (var (url, mailboxes) in wanted)
            {
                var fingerprint = Fingerprint(provider!.Platform, token!, mailboxes);
                state.TryGetValue(url, out var current);
                if (current is not null && current.Fingerprint == fingerprint && _time.GetUtcNow() - current.RenewedAt < RenewAfter)
                    continue;

                try
                {
                    state[url] = await RegisterAsync(relayFor(url), current, provider!.Platform, token!, mailboxes, fingerprint, ct);
                }
                catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
                {
                    allOk = false; // keep the old state; a relay without a push gateway answers 501 and simply gets no push
                }
            }

            await SaveAsync(state);
            return allOk;
        }
        finally
        {
            _gate.Release();
        }
    }

    async Task<PushRelayState> RegisterAsync(RelayClient relay, PushRelayState? current, string platform, string token,
        List<string> mailboxes, string fingerprint, CancellationToken ct)
    {
        var now = _time.GetUtcNow();
        if (current is not null)
        {
            var existing = new PushSubscriptionOut(current.Id, current.Secret);
            if (await relay.UpdatePushMailboxesAsync(existing, mailboxes, platform, token, ct))
                return current with { Fingerprint = fingerprint, RenewedAt = now };
            // The relay forgot the subscription (it was idle for too long, or the relay was reset): register again.
        }

        var created = await relay.CreatePushSubscriptionAsync(new PushSubscriptionIn(PushTypes.Gateway, null, mailboxes, platform, token), ct);
        return new PushRelayState(created.Id, created.Secret, fingerprint, now);
    }

    /// <summary>Changes whenever the token or the mailbox list changes; the token itself is not kept.</summary>
    static string Fingerprint(string platform, string token, List<string> mailboxes) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{platform}|{token}|{string.Join(',', mailboxes)}")));

    async Task<Dictionary<string, PushRelayState>> LoadAsync()
    {
        var json = await store.GetAsync();
        if (json is null) return [];
        try { return JsonSerializer.Deserialize(json, CoreJson.Default.DictionaryStringPushRelayState) ?? []; }
        catch (JsonException) { return []; }
    }

    Task SaveAsync(Dictionary<string, PushRelayState> state) =>
        state.Count == 0 ? store.ClearAsync() : store.SetAsync(JsonSerializer.Serialize(state, CoreJson.Default.DictionaryStringPushRelayState));
}

/// <summary>Decides which posts a wake should announce.</summary>
public static class PostNotifications
{
    /// <summary>Posts by others that are not in <paramref name="known"/> yet, oldest first.</summary>
    public static List<FeedPost> NewPosts(BubbleSnapshot snapshot, IReadOnlySet<string> known) =>
        [.. snapshot.Posts.Where(p => !p.IsMine && !known.Contains(p.Id)).OrderBy(p => p.Message.CreatedAt)];

    /// <summary>A line for the notification, e.g. "Anna 🥳 Sunrise at the lake". Never longer than about 100 characters.</summary>
    public static string Describe(FeedPost post)
    {
        var text = post.Message.Text?.ReplaceLineEndings(" ").Trim();
        var line = string.Join(' ', new[] { post.Message.AuthorName, post.Message.Emoji, text }.Where(s => !string.IsNullOrWhiteSpace(s)));
        return line.Length <= 100 ? line : line[..99].TrimEnd() + "…";
    }
}
