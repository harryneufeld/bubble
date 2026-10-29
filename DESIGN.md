---
version: 2
name: Bubble
description: Dunkle, lebendige Social-App für kleine private Kreise. Tiefes Schwarzviolett als Grund, Fotos und leuchtende Verläufe (Pink → Violett → Cyan, wie das Schillern einer Seifenblase) als Gestaltung. Stories-Leiste, Vollbild-Stories und Foto-Karten wie in sozialen Apps – aber ohne Likes-Zähler, Streaks oder endlosen Feed.

colors:
  # Flächen (dunkel)
  canvas: "#0B0A10"
  surface: "#17151F"
  surface-raised: "#221F2C"
  hairline: "#2E2A3A"

  # Text
  ink: "#F5F3FA"
  ink-soft: "#C9C4D6"     # >= 4.5:1 auf canvas und surface
  muted: "#8F89A0"        # nur Metadaten, >= 4.5:1 auf canvas

  # Schillern – Akzentverlauf für Ringe, Cover, Deko
  pink: "#FF4F9A"
  violet: "#8B5CF6"
  cyan: "#22D3EE"
  accent: "#B9A2FF"       # Text-Links und Ghost-Buttons

  # Hauptaktion: Verlauf cta-start → cta-end, weißer Text (>= 4.5:1)
  cta-start: "#D9246F"
  cta-end: "#6D3CF5"
  on-cta: "#FFFFFF"

  # Stimmung des Tages – freundlich, nicht wertend (kein Rot/Grün)
  top: "#3A2D0E"
  on-top: "#FFD27A"       # Sonne
  flop: "#14283A"
  on-flop: "#9CC7FF"      # Regenwolke

  # Semantik
  error: "#FF5A6E"
  success: "#4ADE80"

typography:
  # Display: Fraunces (nur Wortmarke) · UI: Inter (SIL OFL). Beide liegen als TTF in der App.
  wordmark: { fontFamily: Fraunces, fontSize: 30, fontWeight: 400 }
  hero:     { fontFamily: Inter, fontSize: 36, fontWeight: 700, lineHeight: 1.05, letterSpacing: -1 }
  title:    { fontFamily: Inter, fontSize: 26, fontWeight: 700, letterSpacing: -0.5 }
  heading:  { fontFamily: Inter, fontSize: 17, fontWeight: 600 }
  body:     { fontFamily: Inter, fontSize: 16, fontWeight: 400, lineHeight: 1.3 }
  caption:  { fontFamily: Inter, fontSize: 13, fontWeight: 500 }
  section:  { fontFamily: Inter, fontSize: 13, fontWeight: 600, letterSpacing: 1, textTransform: uppercase }
  code:     { fontFamily: monospace, fontSize: 26, fontWeight: 700, letterSpacing: 3 }

rounded:
  field: 16
  card: 24
  photo: 28
  full: 9999

spacing:
  xxs: 4
  xs: 8
  sm: 12
  md: 16     # Seitenrand
  lg: 24
  xl: 32

components:
  button-primary:   { background: "gradient(cta-start → cta-end)", textColor: "{colors.on-cta}", rounded: "{rounded.full}", height: 56 }
  button-secondary: { backgroundColor: "{colors.surface-raised}", textColor: "{colors.ink}", rounded: "{rounded.full}", height: 48 }
  button-ghost:     { backgroundColor: transparent, textColor: "{colors.accent}", rounded: "{rounded.full}" }
  icon-button:      { backgroundColor: "{colors.surface-raised}", rounded: "{rounded.full}", size: 44 }
  card:             { backgroundColor: "{colors.surface}", rounded: "{rounded.card}", padding: 0 }
  text-input:       { backgroundColor: "{colors.surface}", borderColor: "{colors.hairline}", rounded: "{rounded.field}", height: 54 }
  secret-box:       { backgroundColor: "{colors.surface}", border: "2px gradient(pink → violet → cyan)", rounded: "{rounded.card}" }
  avatar:           { shape: circle, fallback: "Initialen auf Verlauf, stabil je Name" }
  story-ring:       { new: "gradient(pink → violet → cyan)", seen: "#3A3548" }
---

# Bubble – Design-Guidelines

Diese Datei ist verbindlich für jede Oberfläche von Bubble. Menschen und KI-Agenten bauen danach. Die Vorlagen in [docs/design/references](docs/design/references/README.md) sind nur Inspiration.

## 1. Stimmung

Bubble fühlt sich an wie **die Stories-Leiste einer sozialen App, nur für deine engsten Leute**: dunkel, lebendig, fotolastig. Die Oberfläche tritt zurück, Fotos und leuchtende Verläufe tragen die Gestaltung.

- **Dunkler Grund.** Die App ist immer dunkel (`UserAppTheme = Dark`). Kein reines Schwarz, sondern `canvas` mit leichtem Violett.
- **Schillern statt Flächenfarbe.** Pink → Violett → Cyan erscheint an wenigen Stellen: Story-Ringe, Cover, Hauptbutton, Deko. Nie als Text- oder Hintergrundfarbe für Fließtext.
- **Fotos zuerst.** Karten laufen randlos bis an den Rand. Text-Posts bekommen einen leuchtenden Verlauf, damit sie sich wie Fotos anfühlen.
- **Sozial, aber ruhig.** Siehe Abschnitt 7: keine Zähler, keine Streaks, kein Sog.

## 2. Farben und ihre Rollen

| Rolle | Token | Regel |
|---|---|---|
| Hauptaktion | `cta-start → cta-end` | **Genau eine** pro Ansicht („Teilen“, „Los geht's“, „Beitreten“). Dazu darf in Kopfzeilen ein runder `+`-Icon-Button im selben Verlauf stehen. |
| Grund | `canvas` | Seitenhintergrund. |
| Karten, Felder | `surface` (+ `hairline` bei Feldern) | Keine Rahmen um Karten, nur Fläche auf Grund. |
| Hervorgehoben | `surface-raised` | Sekundärbuttons, Chips, Icon-Buttons. |
| Fließtext | `ink`, `ink-soft` | `muted` nur für Metadaten wie Zeit und Server. |
| Links, Ghost-Aktionen | `accent` | Nicht für Fließtext. |
| Schillern | `pink`, `violet`, `cyan` | Ringe, Cover, Deko (siehe oben). |
| Top / Flop | `top`/`on-top`, `flop`/`on-flop` | Bewusst **nicht** Grün/Rot: Ein schlechter Tag ist kein Fehler. |
| Fehler | `error` | Nur für echte Fehler, nie für „Flop“. |

**Kontrast:** Text mindestens 4,5:1, große Schrift und Icons mindestens 3:1. Weißer Text auf Verläufen nutzt die dunkleren Stufen (`cta-*`, Avatar- und Post-Verläufe). Text auf Fotos steht immer auf einem Schatten-Verlauf (`PhotoShade`).

## 3. Typografie

- **Inter** für alles, Gewichte 400/500/600/700. Schlagzeilen sind fett und eng gesetzt (`hero`, `title`).
- **Fraunces** (Serif) nur für die Wortmarke „bubble“. Sie bleibt als Wiedererkennung.
- **Backup- und Einladungscodes** in Monospace, groß, mit Zeichenabstand.
- Dynamic Type bzw. Systemschriftgrößen werden respektiert; Layouts müssen bei 200 % funktionieren.
- **Schriften liegen in der App** (`Resources/Fonts`, Lizenzen in `docs/licenses`). Nie von einem CDN laden – das würde die IP der Nutzer an Dritte senden.
- Statische TTFs, weil MAUI keine Achsen variabler Schriften ansteuert. Aliase: `Display`, `Body`, `BodyMedium`, `BodySemibold`, `BodyBold`.

## 4. Komponenten

**Navigation.** Drei Tabs unten: **Heute**, **Bubbles**, **Profil**, mit Linien-Icons (2 px, `ic_*.svg`). Ausgewählt = `ink`, sonst `muted`. Detailseiten (Bubble, Einladen, Beitreten, Teilen, Backup) blenden die Tab-Leiste aus.

**Avatar.** Rund. Ohne Foto: Initialen auf stabilem Verlauf je Name. Optional mit Story-Ring (`new` = Verlauf, `seen` = dezent) und Plus-Badge („Du“ in der Story-Leiste).

**Story-Leiste (Heute).** Horizontale Reihe: zuerst „Du“ (teilen), dann eine Bubble pro Kreis mit Highlights der letzten 24 h. Ungesehene zuerst mit Verlaufsring, gesehene dezent. Antippen öffnet die Vollbild-Ansicht.

**Story-Ansicht.** Vollbild auf Schwarz: Fortschrittsbalken oben (6 s pro Story), Absender und Schließen-Button, Stimmungs-Pille und Bildunterschrift unten auf Schatten. Tippen rechts = weiter, links = zurück. Beginnt bei der ersten ungesehenen Story.

**Post-Karte.**
```
┌──────────────────────────────────┐
│ (Avatar) Anna          [👍 Top]  │  Kopf: Name, „Bubble · vor 2 h“, Stimmung
│ ┌──────────────────────────────┐ │
│ │ Foto, randlos       [⏳ 14 h]│ │  Glas-Pille mit Lebensdauer
│ └──────────────────────────────┘ │
│ Sonnenaufgang am See             │
└──────────────────────────────────┘
```
Nur-Text-Posts: großer weißer Text mittig auf leuchtendem Verlauf, gleiche Höhe wie ein Foto-Post.

**Bubble-Kachel (Bubbles).** Zwei Spalten. Cover = neuestes Foto oder Verlauf, unten Schatten, Gesichter der Mitglieder, Name, „n Leute · Lebensdauer“. Ein Punkt oben rechts zeigt Neues – **kein Zähler**.

**Bubble-Seite.** Cover-Kopf mit Name, Lebensdauer- und Leute-Pille; Mitglieder-Reihe; „Leute einladen“ und „Lebensdauer“; darunter die Highlights. Teilen-Button unten.

**Geheimnis-Box.** Rahmen im Schillern-Verlauf, enthält immer: den Wert, eine Kopieren-Aktion und einen Satz, wo man ihn sicher aufbewahrt.

**Leerzustände.** Deko aus drei Blasen im Verlauf, eine Titelzeile, ein Satz, die eine passende Aktion.

## 5. Layout

- Seitenrand 16 pt, Kartenabstand 16 pt. Eine Spalte auf dem Handy.
- Auf Tablet und Desktop ist die Inhaltsspalte höchstens 600 pt breit und zentriert.
- Die Hauptaktion sitzt unten und ist mit dem Daumen erreichbar.
- Touch-Ziele mindestens 44×44 pt.

## 6. Tiefe

- Tiefe entsteht über `surface` auf `canvas`, nicht über Schatten.
- Auf Fotos liegen halbtransparente Glas-Pillen (`#8C0B0A10`) für Lebensdauer und Stimmung.
- Gedrückte Buttons skalieren leicht (0,97). Seitenwechsel im Onboarding blenden ein und gleiten 24 pt hoch (250 ms). Animationen berücksichtigen „Bewegung reduzieren“.

## 7. Bubble-spezifische Regeln

**Freunde hinzufügen = in eine Bubble einladen**
- Einladen: QR-Code (enthält den vollständigen Link), kurzer Code (z. B. `7KQ2M-9XH4T`, 24 h gültig) oder geteilter Link. Der kurze Code liegt verschlüsselt als 24h-Nachricht auf dem Relay.
- Beitreten: Kamera scannt den QR-Code, oder man gibt den Code ein bzw. fügt den Link ein. Vor dem Beitreten erscheint eine Vorschau mit Name, Lebensdauer und Server.
- Vor jeder Einladung steht die Erklärung, dass QR-Code, Code und Link den Schlüssel enthalten.

**Datenschutz sichtbar, aber leise**
- Jeder Post zeigt seine Lebensdauer (⏳/📌). Vor dem Teilen steht, was mit dem Post passiert.
- Beim Foto-Hinzufügen steht „Standort- und Kameradaten werden entfernt“.
- Das Profil (Name, Bild, Bio) geht nur verschlüsselt an die eigenen Bubbles.
- Server-Adressen und Fehlerursachen werden konkret benannt, z. B. „192.168.1.20:5080 ist nicht erreichbar“.

**Keine Aufmerksamkeitsfallen**
- Kein endloser Feed (nur Highlights der Bubbles, kein Nachladen ins Unendliche), keine Streaks, keine Like-Zähler, keine „X hat noch nicht gepostet“-Hinweise, keine Zähler für Ungelesenes.
- Reaktionen (später) sind Emojis ohne öffentliche Zählung.
- Benachrichtigungen höchstens einmal täglich und inhaltsleer.

**Sprache**
- Wir duzen, sind warm und kurz. Wir sagen „Highlight“, „Bubble“ und „teilen“, nicht „Content“, „Gruppe“ oder „posten“.
- Fehler sagen, was passiert ist und was man tun kann. Die Ausnahme ist die Server-Adresse.
- Ein Flop-Tag wird nie kommentiert oder bewertet.

## 8. Do's and Don'ts

### Do
- `canvas` als Hintergrund jeder Seite verwenden, Karten in `surface`.
- Genau einen Hauptbutton im Verlauf pro Ansicht setzen.
- Fotos groß und randlos zeigen; Text auf Fotos nur über Schatten.
- Lebensdauer an jedem Post anzeigen.
- Erklären, warum etwas deaktiviert ist („✓ Heute schon geteilt“).
- Linien-Icons aus `Resources/Images` verwenden (keine Emojis als Navigation).

### Don't
- Das Schillern nicht für Fließtext oder große Flächen hinter Text mit niedrigem Kontrast nutzen.
- Top/Flop nicht in Grün/Rot einfärben.
- Keine Schriften oder Bilder von externen Servern laden.
- Keine Zähler, Streaks oder Gamification einbauen.
- Keine Rahmen um Karten und keine harten Schatten.

## 9. Responsive Verhalten

| Breite | Verhalten |
|---|---|
| < 600 pt | eine Spalte, Tab-Leiste unten, Hauptaktion unten fixiert |
| ≥ 600 pt | Inhalt zentriert, höchstens 600 pt breit |

## 10. Story-Editor (später)

Die Leinwand hat das Format **9:16** (1080×1920), wird skaliert und nie beschnitten; Export als flaches JPEG. Hintergründe nutzen Verläufe aus `pink`, `violet`, `cyan` und deren Nachbarn. Die Werkzeugleiste ist ruhige App-Oberfläche (`surface-raised`, schwebend).

## 11. Umsetzung in .NET MAUI

- Farben und Verläufe: `Resources/Styles/Colors.xaml` (`Canvas`, `Surface`, `SurfaceRaised`, `Hairline`, `Ink`, `InkSoft`, `Muted`, `Pink`, `Violet`, `Cyan`, `Accent`, `CtaStart`, `CtaEnd`, `Top`/`OnTop`, `Flop`/`OnFlop`; Brushes `BubbleGradient`, `CtaGradient`, `PhotoShade`).
- Komponenten: benannte Styles in `Resources/Styles/Styles.xaml` (`PrimaryButton`, `SecondaryButton`, `GhostButton`, `IconButton`, `Card`, `Field`, `InfoBar`, `SecretBox`, `GlassPill`, `ChipTop`, `ChipFlop`, `HeroText`, `TitleText`, `CaptionText`, `SectionText`, `CodeText`).
- Wiederverwendbare Controls: `Controls/AvatarView` (Avatar, Ring, Plus), `Controls/PostCard` (Post-Karte).
- QR-Codes erzeugt `QRCoder` lokal, gescannt wird mit `ZXing.Net.Maui` (Kamera nur, solange der Scan-Tab offen ist).

## 12. Prompt-Hilfe für KI-Agenten

> Baue die Oberfläche nach `DESIGN.md`: dunkler Grund (`canvas` #0B0A10), Karten in `surface` ohne Rahmen mit 24-pt-Radius, genau ein Pillen-Button im Pink-Violett-Verlauf pro Ansicht. Inter fett für Schlagzeilen, Fraunces nur für die Wortmarke. Fotos randlos und groß, Text-Posts auf leuchtendem Verlauf. Story-Ringe im Pink-Violett-Cyan-Verlauf. Jeder Post zeigt Top/Flop-Pille (Sonne/Regenwolke, nicht grün/rot) und Lebensdauer. Keine Zähler, keine Streaks, keine externen Ressourcen. Deutsch, Du-Form, warm und kurz.
