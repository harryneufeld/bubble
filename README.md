# 🫧 Bubble

Share highlights with a feeling (emoji) in private groups, with hearts and comments, end-to-end encrypted. Goals and roadmap are in [PLAN.md](PLAN.md), the design rules in [DESIGN.md](DESIGN.md).

[![Sponsor Bubble](https://img.shields.io/badge/Sponsor-Bubble-ea4aaa?logo=githubsponsors&logoColor=white)](https://github.com/sponsors/harryneufeld)

## Support Bubble

Bubble has no ads, no tracking and no investors. It is built in my spare time, and keeping it alive costs real money: the relay server in the EU, the domain and website, and the fees for publishing the app in the stores. If Bubble brings you and your people a little closer, a coffee's worth helps keep it running:

- ❤️ [Sponsor on GitHub](https://github.com/sponsors/harryneufeld), one-time or monthly
- ⭐ Star the repo and tell a friend, that helps too

Thank you!

## What's in the app (1.6.0)

- **Stories:** a row of rings on top, "You" first. Highlights from the last 24 hours play one after another, 6 seconds each.
- **This week:** Monday to Sunday, each day with a small stack of its photos; tap a day to see its highlights.
- **On this day:** what was shared a year ago today (or more years, else a month ago), when there is something.
- **Most loved:** the highlights your people loved most, from months and years ago. Only highlights older than four weeks count; hearts plus twice the comments, at most one per month.
- **Rewind:** every highlight that is still there, month by month. Filter by all, mine or one bubble, scrub through time on the right, or let "Surprise me" pick one.
- **Highlights full screen:** opened highlights keep their own format (4:5 or 9:16) and are never cut off; caption, hearts and comments follow when you scroll down.
- **Profiles of friends:** tap someone's face to see their picture, bio, the bubbles you share and their highlights.
- **Nothing is kept longer than the post:** a highlight that is not remembered disappears after 24 hours, from the relay and from every view in the app. There is no local archive.

## Projects

| Project | Contents |
|---|---|
| `src/Bubble.Core` | Crypto, message format, invitations, backup, relay client, feed logic, timeline (weeks, months, on this day, most loved). Platform independent. |
| `src/Bubble.Relay` | ASP.NET Core relay with SQLite. Only stores encrypted blobs. |
| `src/Bubble.App` | .NET MAUI app (Android, iOS, Windows). |
| `tests/Bubble.Tests` | Tests for core and relay, including end-to-end through an in-memory relay. |

## Getting started

```bash
dotnet test
```

```bash
dotnet run --project src/Bubble.Relay
```

The relay then runs on `http://localhost:5080`. Start the app from Visual Studio, e.g. as a Windows app or in the Android emulator; the emulator reaches the relay at `10.0.2.2:5080`. For a real phone, start the relay with `--urls http://0.0.0.0:5080` and enter your PC's IP address in the app under "Server".

### Checking performance

Smooth scrolling and page transitions can only be judged with a **release build**: the debug build is compiled at runtime and stutters about three times as often. For Android:

```bash
dotnet build src/Bubble.App -f net10.0-android -c Release -p:AndroidPackageFormat=apk -p:RuntimeIdentifier=android-arm64
```

Measure with `adb shell dumpsys gfxinfo org.bubble.social` (janky frames, percentiles) after scrolling through a few screens.

### Signed release build (Android)

The app ID is `org.bubble.social`. Release builds are signed with their own key, which is **not in the repo**: `~/.bubble-signing/` holds `bubble-release.keystore`, the password files `storepass.txt` and `keypass.txt`, and `signing.props`, which the project picks up automatically (only with `-c Release`; without the file the debug key is used). Back the folder up, because an installed app can't be updated with a new key, and never commit it.

Finished packages for test devices go into `dist/` (not in the repo), e.g. `dist/Bubble-1.1.0-release-arm64.apk`. Install with `adb install <apk>`, or copy the file to the device and open it (allow "Install unknown apps"). The package is for 64-bit ARM devices, which means practically every current phone.

Certificate fingerprint (SHA-256): `AA:A4:0F:25:48:41:80:66:88:83:51:55:DA:AB:DF:26:C5:71:19:E6:FD:56:FA:C6:B5:AE:9B:11:19:3A:FE:47`

### Relay via Docker

```bash
docker compose up -d --build
```

The image builds for amd64 and arm64, and the database lives in the volume `relay-data`. In the app, enter the address during onboarding (also on the "I already have an account" screen) or later under "Server", e.g. `192.168.1.20:5080`.

### Relay on a Raspberry Pi

On a Pi with a 64-bit operating system, [scripts/setup-pi.sh](scripts/setup-pi.sh) sets everything up: it installs Git and Docker if needed, fetches the code and starts the relay with `docker compose`. Running it again updates it.

```bash
scp scripts/setup-pi.sh pi@192.168.1.20:/tmp/bubble-setup.sh
ssh -t pi@192.168.1.20 "bash /tmp/bubble-setup.sh"
```

The first build on the Pi takes a few minutes. The relay listens on HTTP, port 5080; for access from outside, put an HTTPS proxy in front of it.

The live tests run against a running relay:

```bash
BUBBLE_RELAY_URL=http://<server>:5080/ dotnet test --filter LiveRelayTests
```

## How the encryption works

- **Identity:** an Ed25519 key pair on the device, no email or phone number. Every message is signed with it.
- **Bubble:** a random 32-byte root key. HKDF derives from it:
  - the **mailbox ID** the relay sees (random-looking, unrelated to the name),
  - the **content key** for ChaCha20-Poly1305.
- **Invitation:** `bubble://join#…`. The key is in the fragment. The app shows it as a **QR code** (generated locally and scanned with the camera) or as a **short code** like `7KQ2M-9XH4T` (50 bits, valid for 24 h): the app derives a mailbox ID and a key from the code with Argon2id, and the invitation sits there on the relay, encrypted, as a 24h message. The relay sees neither the code nor the bubble.
- **Hearts and comments:** their own signed, encrypted messages (`Like`, `Comment`) carrying the post ID. For hearts, the newest message per person counts. Both expire with the post: on the relay they carry the same expiry time as the post and are deleted with it.
- **Profile:** name, profile picture (512 × 512, no metadata) and bio go as a signed, encrypted `Profile` message to every bubble of the member. The relay knows nothing about it.
- **Message:** JSON → sign → encrypt. Mailbox and message ID are bound as associated data, so the server can neither modify messages nor replay them under a new ID.
- **Lifetime:** 24h posts get an `expiresAt`. The relay deletes them for good, and the client hides them itself as well. If someone changes the lifetime, a signed `LifetimeChanged` message is sent; it only applies to new posts.
- **Photos:** are rotated on the device, downscaled to at most 1600 px and re-encoded as JPEG. EXIF and GPS data are dropped in the process.
- **Local storage:** in the platform's `SecureStorage` (Keystore, Keychain, DPAPI). Android cloud backups are disabled.

## Leaving, removing members and logging out

- **Leaving:** any member can leave a bubble (bubble page → "Leave bubble"). The app sends a signed `Left` message so the others stop listing you, and forgets the bubble key on the device.
- **Owner:** whoever creates a bubble is its owner; their public key travels in the invitation. Bubbles created before owners existed have no owner and members cannot be removed from them.
- **Removing a member:** the owner taps a member on the bubble page. Because everyone in a bubble shares one key, removal is a **key rotation**: the owner generates a new root key and posts a signed `KeyRotation` message to the old mailbox that contains the new key sealed separately for each remaining member (X25519 Diffie-Hellman between the owner's and the member's identity, converted from their Ed25519 keys, then ChaCha20-Poly1305). Remaining members pick the new key up the next time they load the bubble and move over automatically. The removed member finds no sealed key for themselves, sees that they were removed and drops the bubble. They can neither read nor write anything new.
  - Earlier highlights stay behind under the old key; the new mailbox starts empty.
  - Only members who have published a profile or shared something receive the new key.
- **Logging out:** Profile → "Log out" deletes the identity and bubble keys from the device. The app warns if no backup exists. With a backup, "I already have an account" and the backup code restore everything.

## Backup and new device

1. During setup the app creates a **backup code** with 25 characters (125 bits), e.g. `7KQ2M-…`. The app recommends saving it in a password manager.
2. **Argon2id** derives three values from the code: key, backup ID and write token.
3. The vault (identity and all bubble keys) is encrypted with a random data key. This data key is "wrapped" per unlock method (`BackupBlob.Wraps`).
4. When something changes, e.g. you join a bubble, the app updates the backup automatically.
5. On a new device the code is enough to restore everything. **The backup lives on the server (relay) you used when you created it, so choose that server first** – the "I already have an account" screen has a "Change server" button for this.

**Passkey (prepared):** a passkey will be added as a second entry in `Wraps`. The WebAuthn PRF extension supplies the key; the data format does not change.

## What the relay knows and what it doesn't

| knows | doesn't know |
|---|---|
| Mailbox IDs, number and size of messages | Names of bubbles or people, content, photos |
| Expiry time of 24h posts | who is in which bubble (no accounts) |
| IP addresses during the request (not logged or stored) | contents of backups |

## Deliberate gaps in the MVP

- **Member management is basic:** removal needs the owner online and only reaches members the owner has seen in the bubble; there is no admin transfer and no way for others to veto. Ideally this moves to MLS later.
- **No rate limiting** and no storage quotas on the relay.
- **Data loading:** every bubble is loaded completely on each refresh; the app first shows what it loaded last time from a cache on the device.
- **Short invitation codes are guessable:** 50 bits, but only valid for 24 h and slowed down by Argon2id. Prefer the QR code or link if in doubt.
- **Push notifications** work on Android through FCM only if the build has Firebase settings and the relay has a push gateway, see [docs/push.md](docs/push.md). Not yet: UnifiedPush in the app, iOS.
- **Not included yet:** passkey, emoji reactions.
- **Development only:** HTTP is allowed for the local relay (Android `usesCleartextTraffic`, iOS `NSAllowsLocalNetworking`). Production needs HTTPS and a relay in the EU.
