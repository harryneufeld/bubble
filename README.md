# 🫧 Bubble

Share highlights with a feeling (emoji) in private groups, with hearts and comments, end-to-end encrypted. Goals and roadmap are in [PLAN.md](PLAN.md), the design rules in [DESIGN.md](DESIGN.md).

[![Sponsor Bubble](https://img.shields.io/badge/Sponsor-Bubble-ea4aaa?logo=githubsponsors&logoColor=white)](https://github.com/sponsors/harryneufeld)

## Support Bubble

Bubble has no ads, no tracking and no investors. It is built in my spare time, and keeping it alive costs real money: the relay server in the EU, the domain and website, and the fees for publishing the app in the stores. If Bubble brings you and your people a little closer, a coffee's worth helps keep it running:

- ❤️ [Sponsor on GitHub](https://github.com/sponsors/harryneufeld), one-time or monthly
- ⭐ Star the repo and tell a friend, that helps too

Thank you!

## Projects

| Project | Contents |
|---|---|
| `src/Bubble.Core` | Crypto, message format, invitations, backup, relay client, feed logic. Platform independent. |
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

### Leistung prüfen

Flüssigkeit beim Scrollen und Seitenwechseln lässt sich nur mit einem **Release-Build** beurteilen: Der Debug-Build wird zur Laufzeit übersetzt und ruckelt etwa dreimal so oft. Für Android:

```bash
dotnet build src/Bubble.App -f net10.0-android -c Release -p:AndroidPackageFormat=apk -p:RuntimeIdentifier=android-arm64
```

Messen lässt sich mit `adb shell dumpsys gfxinfo org.bubble.social` (ruckelnde Frames, Perzentile) nach einem Scrollen über mehrere Karten.

### Signierter Release-Build (Android)

App-ID ist `org.bubble.social`. Release-Builds werden mit einem eigenen Schlüssel signiert, der **nicht im Repo** liegt: `~/.bubble-signing/` enthält `bubble-release.keystore`, die Passwortdateien `storepass.txt` und `keypass.txt` und `signing.props`, die das Projekt automatisch einbindet (nur bei `-c Release`; ohne die Datei wird mit dem Debug-Schlüssel signiert). Den Ordner sichern, denn mit einem neuen Schlüssel lässt sich eine installierte App nicht mehr aktualisieren, und niemals einchecken.

Fertige Pakete für Testgeräte liegen in `dist/` (nicht im Repo), z. B. `dist/Bubble-1.1.0-release-arm64.apk`. Installieren per `adb install <apk>` oder die Datei aufs Gerät kopieren und öffnen („Unbekannte Apps installieren“ erlauben). Das Paket ist für 64-Bit-ARM-Geräte, also praktisch jedes aktuelle Handy.

Zertifikat-Fingerabdruck (SHA-256): `AA:A4:0F:25:48:41:80:66:88:83:51:55:DA:AB:DF:26:C5:71:19:E6:FD:56:FA:C6:B5:AE:9B:11:19:3A:FE:47`

### Relay via Docker

```bash
docker compose up -d --build
```

The image builds for amd64 and arm64, and the database lives in the volume `relay-data`. In the app, enter the address during onboarding (also on the "I already have an account" screen) or later under "Server", e.g. `192.168.1.20:5080`.

### Relay auf einem Raspberry Pi

Auf einem Pi mit 64-Bit-Betriebssystem richtet [scripts/setup-pi.sh](scripts/setup-pi.sh) alles ein: Es installiert bei Bedarf Git und Docker, holt den Code und startet den Relay per `docker compose`. Erneut ausführen aktualisiert.

```bash
scp scripts/setup-pi.sh pi@192.168.1.20:/tmp/bubble-setup.sh
ssh -t pi@192.168.1.20 "bash /tmp/bubble-setup.sh"
```

Der erste Build auf dem Pi dauert ein paar Minuten. Der Relay hört auf HTTP, Port 5080; für Zugriff von außen gehört ein HTTPS-Proxy davor.

Gegen einen laufenden Relay lassen sich die Live-Tests ausführen:

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
- **Data loading:** the feed is loaded completely every time it opens, there is no local cache.
- **Short invitation codes are guessable:** 50 bits, but only valid for 24 h and slowed down by Argon2id. Prefer the QR code or link if in doubt.
- **Push notifications** work on Android through FCM only if the build has Firebase settings and the relay has a push gateway, see [docs/push.md](docs/push.md). Not yet: UnifiedPush in the app, iOS.
- **Not included yet:** passkey, emoji reactions.
- **Development only:** HTTP is allowed for the local relay (Android `usesCleartextTraffic`, iOS `NSAllowsLocalNetworking`). Production needs HTTPS and a relay in the EU.
