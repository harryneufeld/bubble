# 🫧 Bubble

Ein tägliches Highlight (Top oder Flop) mit privaten Gruppen teilen, Ende-zu-Ende-verschlüsselt. Ziel und Roadmap stehen in [PLAN.md](PLAN.md).

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

## So funktioniert die Verschlüsselung

- **Identität:** Ed25519-Schlüsselpaar auf dem Gerät, ohne E-Mail oder Telefonnummer. Jede Nachricht wird damit signiert.
- **Bubble:** ein zufälliger 32-Byte-Root-Key. Per HKDF werden daraus abgeleitet:
  - die **Mailbox-ID**, die der Relay sieht (zufällig aussehend, ohne Bezug zum Namen),
  - der **Inhaltsschlüssel** für ChaCha20-Poly1305.
- **Einladung:** `bubble://join#…`. Der Schlüssel steht im Fragment.
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
- **Noch nicht enthalten:** Push-Benachrichtigungen, Editor wie Canva, Passkey.
- **Nur Entwicklung:** HTTP ist für den lokalen Relay erlaubt (Android `usesCleartextTraffic`, iOS `NSAllowsLocalNetworking`). Für Produktion braucht es HTTPS und einen Relay in der EU.
