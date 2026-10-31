# Push notifications – plan

Goal: the app learns about new posts, hearts and comments without polling, and nobody along the way (relay, push gateway, Google, Apple) learns what was posted or by whom. Self-hosted relays and forks of the app must work without depending on us.

## Principle: pushes only wake the app

A push carries no content. It only says "something is new". The app wakes up, fetches from its relay as usual, decrypts and builds the notification text **on the device** (e.g. "Anna in Family: 🥳"). Relay, gateway and Google/Apple only ever see the wake signal.

## Two delivery paths

```
                       ┌─► Push gateway ──► FCM  ──► Android
Relay (ours or self-  ─┤                └─► APNs ──► iOS
hosted) on new message │
                       └─► UnifiedPush distributor (ntfy, NextPush, …) ──► Android
```

| | Push gateway (FCM/APNs) | UnifiedPush |
|---|---|---|
| Platforms | Android and iOS | Android only |
| Needs Google services | Android: yes | no |
| User setup | none | install a distributor app (e.g. ntfy) |
| Who runs it | whoever publishes the app build (us, or a fork) | the user, or any public ntfy server |
| Default | yes | opt-in in the settings |

The relay doesn't care which path a device uses. It only stores `{ type, endpoint }` and POSTs to it.

## Components

### 1. Push gateway (`src/Bubble.PushGateway`, new)

Small ASP.NET Core service in this repo, with its own Docker image (`ghcr.io/<owner>/bubble-push-gateway`) and CI workflow like the relay. Only the keys are private.

- **Stateless, no database.** On registration the gateway gets `{ platform, token }` and returns a **handle**: `{ platform, token, expiry }` sealed with AES-256-GCM under the gateway secret (old secrets stay accepted for rotation). The relay stores only the handle. It sees the raw token once, while passing the registration through, and does not keep it.
- Who calls it: only the relay. The app registers through its relay (`POST /v1/push/subscriptions` with `type: "gateway"`, `platform`, `token`), the relay calls the gateway. So the gateway can stay on a private network, or be public for other people's relays.
- `POST /v1/register` `{ platform: "fcm" | "apns", token }` → `{ handle }` (`apns` answers 501 until iOS is built; `503` if FCM isn't configured)
- `POST /v1/wake/{handle}` → decrypts, sends a content-free message (the relay just POSTs `{"v":1}` to the stored endpoint URL, same as for UnifiedPush):
  - FCM: data message, high priority, `{ "v": 1 }`
  - APNs: `content-available: 1` plus `mutable-content: 1` with a generic alert, so the iOS notification service extension can replace the text
- Responses: `202` sent, `410 Gone` if the handle is invalid or expired or FCM/APNs reports the token as invalid (the relay deletes the subscription), `429` throttled, `502` the platform failed.
- **Rate limit:** per handle (one wake per 10 s) and per calling IP. Handles expire after 90 days; the app renews them.
- Request logging of ASP.NET is set to `Warning`, so handles in URLs don't end up in logs.
- Configuration via environment variables / mounted files:
  - `Push__Secret`: key for the handles, e.g. `openssl rand -base64 32` (required)
  - `Push__PreviousSecrets__0`, …: older secrets that are still accepted (rotation)
  - `Push__HandleLifetimeDays` (90), `Push__MinWakeInterval` (`00:00:10`)
  - `Push__Fcm__CredentialsFile`: Firebase service account JSON
  - `Push__Apns__KeyFile`, `Push__Apns__KeyId`, `Push__Apns__TeamId`, `Push__Apns__Topic` (only if iOS goes directly through APNs, see open questions)

### 2. Relay (`src/Bubble.Relay`)

- `POST /v1/push/subscriptions` → `{ id, secret }` (max. 200 mailboxes)
  - `{ type: "gateway", platform: "fcm" | "apns", token, mailboxes }`: the relay swaps the token for a handle at its configured gateway (`Push__Gateway=http://gateway:5090`) and stores `<gateway>/v1/wake/<handle>`. `501` if the relay has no gateway, `502` if the gateway refuses or is down.
  - `{ type: "unifiedpush", endpoint, mailboxes }`: endpoint = the URL from the distributor
- `PUT /v1/push/subscriptions/{id}` (header `Authorization: Bearer <secret>`): replace the mailbox list (after joining or leaving a bubble, key rotation). Gateway subscriptions can send `platform` and `token` as well to renew the handle.
- `DELETE /v1/push/subscriptions/{id}` (same header)
- On every `POST /v1/mailboxes/{mailbox}/messages`: for each subscription on that mailbox, queue a wake. A background worker sends it, at most once per subscription every few seconds. Errors are retried briefly; on `404`/`410` the subscription is deleted.
- UnifiedPush: `POST` to the endpoint with body `{"v":1}` (RFC 8030 style). **SSRF protection:** HTTPS only, no private/local addresses, checked when registering and again when connecting (so a DNS name can't point inside); no redirects. `Push__AllowPrivateEndpoints=true` lifts this for local tests.
- The gateway URL is the operator's choice (`Push__Gateway`), so it may be on a private network. Clients can't point the relay at a gateway or any other URL except for UnifiedPush endpoints, which get the SSRF checks.
- Subscriptions with no activity for 60 days are deleted; the app re-registers on start.

### 3. App (`src/Bubble.App`)

- `IPushProvider` with `RegisterAsync() → PushEndpoint`, `UnregisterAsync()`, event `EndpointChanged`:
  - `FcmPushProvider` (Android, native `FirebaseMessagingService`)
  - `ApnsPushProvider` (iOS, or FCM on iOS, see open questions)
  - `UnifiedPushProvider` (Android, UnifiedPush connector library via binding)
  - `NoPushProvider` (fallback: fetch on start and on pull-to-refresh as today)
- `PushRegistrar` in `Bubble.Core`: keeps the relay subscription in sync with the mailbox list. It runs on start, after token changes, after joining or leaving a bubble, and after changing servers.
- On a wake: fetch new messages, decrypt, show a local notification per bubble (grouped). Own posts don't notify.
- iOS: a **notification service extension** fetches and decrypts within the ~30 s it gets and replaces the generic text. If that fails, the generic text "New in your bubbles" stays.
- Settings → Notifications: on/off, path (Standard / UnifiedPush, only if a distributor is installed), per-bubble mute (local only, the relay still wakes the device).

### Configuration for forks (build time)

`push.props` (MSBuild), overridable by forks without code changes:

```xml
<PropertyGroup>
  <BubblePushProvider>fcm</BubblePushProvider>   <!-- fcm | unifiedpush | none -->
  <BubblePushGatewayUrl>https://push.bubble.example/</BubblePushGatewayUrl>
  <BubbleGoogleServicesJson>google-services.json</BubbleGoogleServicesJson>
</PropertyGroup>
```

- A generated `PushConfig` class takes over the values.
- `google-services.json` and `GoogleService-Info.plist` are **not** in the repo. If they're missing, the build leaves out FCM, and only UnifiedPush or "none" remains. A plain fork builds without Firebase.
- A fork with its own Firebase project runs its own gateway with the same code and sets `BubblePushGatewayUrl`.

## Privacy

| Who | Sees | Doesn't see |
|---|---|---|
| Relay | which mailboxes one device subscribes to, gateway URL / UnifiedPush endpoint | content, FCM/APNs token, names |
| Push gateway | that some relay wants to wake device X, and when, plus the relay's IP | mailboxes, bubbles, content |
| Google / Apple | that the app gets a push, and when | content, relay, bubbles |
| UnifiedPush server | endpoint, time | content, mailboxes |

**Known trade-off:** a subscription links the mailboxes of one device on the relay. Without push, the relay can only guess that link from IPs and timing. Mitigation for later: one subscription per bubble with separate handles/endpoints (more registrations, same wake). Push stays optional so users can turn it off.

## Setting it up

1. **Firebase:** create a project, register the Android app `org.bubble.social` with the SHA-1 of the signing key (for Play builds also the Play app-signing key). Download `google-services.json` and a service account key.
2. **App build:** copy the four values from `google-services.json` into [src/Bubble.App/push.props](../src/Bubble.App/push.props) (or pass them as `-p:BubblePushFcm…=…`). Without them the app builds without Firebase, and the Notifications switch doesn't show.
3. **Gateway:** run the image with `Push__Secret` and `Push__Fcm__CredentialsFile` (the service account key).
4. **Relay:** set `Push__Gateway` to the gateway's address (`http://gateway:5090` on the same Docker network).
5. **In the app:** Profile → Notifications. The app asks for the notification permission, registers at the relay of each bubble and renews the handle every two weeks.

How the app behaves:

- One subscription per relay, with the mailboxes of the bubbles on that relay. It follows the bubble list and the token; nothing is sent when nothing changed.
- A wake fetches the bubbles, decrypts, and announces posts by others that weren't seen yet. The first time a bubble is seen its backlog is not announced. What the feed has shown counts as seen.
- The lock screen shows only "New in your bubbles".
- Logging out removes the subscriptions.
- The Firebase package is pinned to 124.1.2: newer ones need AndroidX versions that clash with MAUI 10.0.20.

## Steps

1. **Relay** (done): subscription endpoints, store, wake worker, UnifiedPush delivery with SSRF protection, tests (including in-memory end-to-end with a fake gateway).
2. **Push gateway** (done for FCM; APNs follows with iOS): register/wake, handle sealing, FCM sending, rate limit, Dockerfile, CI workflow. The compose file has a `push` profile for it.
3. **App Android** (done for FCM): `IPushProvider` and `PushRegistrar` in Core, FCM provider and push service, local notifications, "Notifications" switch on the profile page, `push.props`.
4. **App Android UnifiedPush:** connector binding, provider, test with ntfy.
5. **App iOS:** APNs registration, notification service extension (requires an Apple Developer account).
6. **Docs:** README sections "Push" and "Self-hosting the push gateway", update "What the relay knows".

## Open questions

- **iOS via FCM or directly via APNs?** FCM: less code, one token type. APNs directly: no Google dependency on iOS. Leaning towards APNs directly, since the gateway is ours anyway.
- Should self-hosted relays be able to use our gateway freely, or only with an API key? Starting free with a rate limit, adding keys if it gets abused.
- Cap on subscriptions per mailbox or per IP at the relay (anyone who knows a mailbox ID can subscribe to wakes).
- Notification text with or without the post's emoji/text excerpt (decided on the device, possibly a setting).
