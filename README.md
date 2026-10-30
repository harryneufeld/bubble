# 🫧 Bubble

Highlights mit einem Gefühl (Emoji) in privaten Gruppen teilen, mit Herzen und Kommentaren, Ende-zu-Ende-verschlüsselt. Ziel und Roadmap stehen in [PLAN.md](PLAN.md), die Gestaltungsregeln in [DESIGN.md](DESIGN.md).

## Projekte

| Projekt | Inhalt |
|---|---|
| `src/Bubble.Core` | Krypto, Nachrichtenformat, Einladungen, Backup, Relay-Client, Feed-Logik. Plattformunabhängig. |
| `src/Bubble.Relay` | ASP.NET-Core-Relay mit SQLite. Speichert nur verschlüsselte Blobs. |
| `src/Bubble.App` | .NET-MAUI-App (Android, iOS, Windows). |
| `tests/Bubble.Tests` | Tests für Core und Relay, inklusive End-to-End über einen In-Memory-Relay. |

## Loslegen

```bash
dotnet test
```

```bash
dotnet run --project src/Bubble.Relay
```

Der Relay läuft danach auf `http://localhost:5080`. Die App startest du aus Visual Studio, z. B. als Windows-App oder im Android-Emulator; der Emulator erreicht den Relay über `10.0.2.2:5080`. Für ein echtes Handy startest du den Relay mit `--urls http://0.0.0.0:5080` und trägst in `AccountService.DefaultRelayUrl` die IP-Adresse deines PCs ein.

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

### Relay per Docker

```bash
docker compose up -d --build
```

Das Image baut für amd64 und arm64, und die Datenbank liegt im Volume `relay-data`. In der App trägst du beim Onboarding oder später unter „Server“ die Adresse ein, z. B. `192.168.1.20:5080`.

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

## So funktioniert die Verschlüsselung

- **Identität:** Ed25519-Schlüsselpaar auf dem Gerät, ohne E-Mail oder Telefonnummer. Jede Nachricht wird damit signiert.
- **Bubble:** ein zufälliger 32-Byte-Root-Key. Per HKDF werden daraus abgeleitet:
  - die **Mailbox-ID**, die der Relay sieht (zufällig aussehend, ohne Bezug zum Namen),
  - der **Inhaltsschlüssel** für ChaCha20-Poly1305.
- **Einladung:** `bubble://join#…`. Der Schlüssel steht im Fragment. Die App zeigt ihn als **QR-Code** (wird lokal erzeugt und mit der Kamera gescannt) oder als **kurzen Code** wie `7KQ2M-9XH4T` (50 Bit, 24 h gültig): Aus dem Code leitet die App per Argon2id eine Mailbox-ID und einen Schlüssel ab, unter der die Einladung verschlüsselt als 24h-Nachricht auf dem Relay liegt. Der Relay sieht weder Code noch Bubble.
- **Herzen und Kommentare:** eigene signierte, verschlüsselte Nachrichten (`Like`, `Comment`) mit der ID des Posts. Beim Herz zählt pro Person die neueste Nachricht. Beide laufen mit dem Post ab: Auf dem Relay stehen sie mit demselben Ablaufzeitpunkt wie der Post und werden mit ihm gelöscht.
- **Profil:** Name, Profilbild (512 × 512, ohne Metadaten) und Bio gehen als signierte, verschlüsselte `Profile`-Nachricht an jede Bubble des Mitglieds. Der Relay weiß davon nichts.
- **Nachricht:** JSON → signieren → verschlüsseln. Mailbox und Nachrichten-ID sind als Associated Data gebunden, sodass der Server Nachrichten weder verändern noch unter neuer ID wieder einspielen kann.
- **Lebensdauer:** 24h-Posts bekommen ein `expiresAt`. Der Relay löscht sie endgültig, und der Client blendet sie zusätzlich selbst aus. Ändert jemand die Lebensdauer, wird eine signierte `LifetimeChanged`-Nachricht verschickt; sie gilt nur für neue Posts.
- **Fotos:** werden auf dem Gerät gedreht, auf maximal 1600 px verkleinert und neu als JPEG kodiert. Dabei fallen EXIF- und GPS-Daten weg.
- **Lokale Speicherung:** im `SecureStorage` der Plattform (Keystore, Keychain, DPAPI). Android-Cloud-Backups sind deaktiviert.

## Backup und neues Gerät

1. Beim Einrichten erzeugt die App einen **Backup-Code** mit 25 Zeichen (125 Bit), z. B. `7KQ2M-…`. Die App rät dazu, ihn im Passwortmanager zu speichern.
2. Per **Argon2id** werden aus dem Code drei Werte abgeleitet: Schlüssel, Backup-ID und Schreib-Token.
3. Der Vault (Identität und alle Bubble-Schlüssel) wird mit einem zufälligen Daten-Schlüssel verschlüsselt. Dieser Daten-Schlüssel ist pro Entsperr-Methode „eingepackt“ (`BackupBlob.Wraps`).
4. Ändert sich etwas, z. B. weil man einer Bubble beitritt, aktualisiert die App das Backup automatisch.
5. Auf einem neuen Gerät genügt der Code, um alles wiederherzustellen.

**Passkey (vorbereitet):** Ein Passkey kommt als zweiter Eintrag in `Wraps` dazu. Den Schlüssel liefert dabei die WebAuthn-PRF-Erweiterung; am Datenformat ändert sich nichts.

## Was der Relay weiß und was nicht

| weiß | weiß nicht |
|---|---|
| Mailbox-IDs, Anzahl und Größe der Nachrichten | Namen von Bubbles oder Personen, Inhalte, Fotos |
| Ablaufzeitpunkt von 24h-Posts | wer in welcher Bubble ist (keine Accounts) |
| IP-Adressen während der Anfrage (werden nicht geloggt oder gespeichert) | Inhalt der Backups |

## Bewusste Lücken im MVP

- **Kein Mitglieder-Management:** Wer den Einladungslink hat, ist drin. Jemanden zu entfernen erfordert Schlüsselrotation (später, idealerweise mit MLS).
- **Kein Rate-Limiting** und keine Speicherquoten auf dem Relay.
- **Daten-Laden:** Der Feed wird bei jedem Öffnen komplett geladen, es gibt keinen lokalen Cache.
- **Kurze Einladungscodes sind erratbar:** 50 Bit, aber nur 24 h gültig und per Argon2id gebremst. Bei Bedarf lieber den QR-Code oder Link nutzen.
- **Noch nicht enthalten:** Push-Benachrichtigungen, Editor wie Canva, Passkey, Emoji-Reaktionen.
- **Nur Entwicklung:** HTTP ist für den lokalen Relay erlaubt (Android `usesCleartextTraffic`, iOS `NSAllowsLocalNetworking`). Für Produktion braucht es HTTPS und einen Relay in der EU.
