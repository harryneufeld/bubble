# Bubble – Plan

## Ziel
Eine App, in der man in privaten Gruppen („Bubbles“) ein tägliches Highlight teilt – Top oder Flop, als Text oder als selbst gestaltetes Bild. Datenschutz steht an erster Stelle: Ende-zu-Ende-verschlüsselt und datensparsam.

## Kernfunktionen
- **Bubbles:** kleine private Gruppen, Beitritt per Einladungslink
- **Highlights teilen**, so oft man mag, markiert als Top 👍 oder Flop 👎
- **Editor wie Canva:** Story-Leinwand (9:16) mit Fotos, Text, Stickern und Vorlagen; geteilt wird das fertige Bild
- **Lebensdauer pro Bubble einstellbar:** ⏳ 24h (wie Stories) oder 📌 dauerhaft
  - jeder Post zeigt ein Badge mit seiner Lebensdauer
  - eine Änderung gilt nur für neue Posts, und die Bubble wird informiert

## Datenschutz
- **Ende-zu-Ende-Verschlüsselung** (libsodium): ein Schlüssel pro Bubble, Rotation, wenn jemand entfernt wird
- **„Blinder“ Relay-Server** in der EU: speichert nur verschlüsselte Blobs und löscht 24h-Posts automatisch
- **Keine E-Mail und keine Telefonnummer:** die Identität ist ein Schlüsselpaar auf dem Gerät
- **Bilder:** Metadaten (EXIF/GPS) werden vor dem Upload entfernt, das Bild wird komprimiert und verschlüsselt
- **Inhaltsleere Push-Benachrichtigungen**, keine IP-Logs, keine externen Assets

## Neues Gerät / Wiederherstellung
Ein verschlüsseltes Schlüssel-Backup liegt auf dem Server. Es enthält die Identität und alle Bubble-Schlüssel und lässt sich auf zwei Wegen öffnen:
1. **Passkey** (WebAuthn PRF), synchronisiert über iCloud oder Google – der Standardweg
2. **Backup-Code** mit dem Hinweis, ihn im Passwortmanager zu speichern – die Alternative

Fallback: Freunde laden einen erneut in die Bubble ein.

## Stack
.NET 10 · .NET MAUI (App) · ASP.NET Core + SQLite (Relay) · BouncyCastle (ChaCha20-Poly1305, Ed25519, Argon2id) · SkiaSharp (Bildbearbeitung, später Editor)

## Roadmap
1. **MVP:** Bubbles, Text- und Foto-Posts, Ende-zu-Ende-Verschlüsselung, 24h/dauerhaft, Passkey- und Code-Backup
2. **Editor:** Leinwand, Vorlagen, Sticker
3. **Komfort:** Reaktionen, Push, Mehrgeräte-Support
4. **Härten:** MLS, Metadatenschutz, Option zum Self-Hosting
