---
version: 1
name: Bubble
description: Warmes, ruhiges Tagebuch-Gefühl für kleine private Kreise. Papierfarbener Grund, ruhige Erdtöne und eine einzige violette Aktionsfarbe (wie das Schillern einer Seifenblase). Fotos tragen die Gestaltung, die Schrift bleibt zurückhaltend. Im Story-Editor darf es bunt und verspielt werden, überall sonst ist es ruhig. Inspiriert von Oura (Basis), Airbnb (Foto-Karten) und Clay (Editor-Farben), siehe docs/design/references.

colors:
  # Aktion – genau eine pro Ansicht
  primary: "#5B47E0"
  primary-pressed: "#4935C4"
  primary-disabled: "#CFC8F5"
  on-primary: "#FFFFFF"

  # Flächen
  canvas: "#F7F1E8"        # warmes Papier – Standard-Hintergrund, nie reines Weiß
  surface: "#FFFFFF"       # Karten, Eingabefelder, Sheets
  surface-sunk: "#EFE7DA"  # Info-Leisten, gedrückte Flächen
  hairline: "#E1DACE"

  # Text
  ink: "#1C1B1A"
  ink-soft: "#4A4741"
  muted: "#6B655C"         # >= 4.5:1 auf canvas

  # Stimmung des Tages – freundlich, nicht wertend (kein Rot/Grün)
  top: "#F3DFAE"           # Sonne
  on-top: "#6B4A0B"
  flop: "#DCE3EA"          # Regenwolke
  on-flop: "#3C4F63"

  # Lebensdauer
  story: "#F1E3CF"         # ⏳ 24 Stunden – Sand
  on-story: "#6B4A1E"
  persistent: "#DDE3D3"    # 📌 dauerhaft – Salbei
  on-persistent: "#3F5235"

  # Semantik
  error: "#C2412D"
  on-error: "#FFFFFF"
  success: "#3F6B45"

  # Story-Editor (nur auf der Leinwand, nie in der App-Oberfläche)
  editor-pink: "#FF6FA3"
  editor-teal: "#2BB3A3"
  editor-lavender: "#B7A6FF"
  editor-peach: "#FFB38A"
  editor-ochre: "#E3A82B"
  editor-sky: "#7CC4FF"
  editor-cream: "#FFF8EC"
  editor-ink: "#141312"

  # Dunkelmodus
  dark-canvas: "#1A1816"
  dark-surface: "#24211E"
  dark-hairline: "#3A3632"
  dark-ink: "#F2ECE3"
  dark-muted: "#ABA399"
  dark-primary: "#9C8FFF"
  dark-on-primary: "#14112B"

typography:
  # Display: Fraunces (variabel, SIL OFL) · UI: Inter (SIL OFL)
  # Beide als TTF in der App mitliefern – niemals zur Laufzeit von einem CDN laden.
  display:   { fontFamily: Fraunces, fontSize: 34, fontWeight: 400, lineHeight: 1.1 }
  title:     { fontFamily: Fraunces, fontSize: 24, fontWeight: 500, lineHeight: 1.2 }
  heading:   { fontFamily: Inter, fontSize: 18, fontWeight: 600, lineHeight: 1.3 }
  body:      { fontFamily: Inter, fontSize: 16, fontWeight: 400, lineHeight: 1.5 }
  body-sm:   { fontFamily: Inter, fontSize: 14, fontWeight: 400, lineHeight: 1.45 }
  button:    { fontFamily: Inter, fontSize: 16, fontWeight: 600, lineHeight: 1 }
  caption:   { fontFamily: Inter, fontSize: 12, fontWeight: 500, lineHeight: 1.3 }
  code:      { fontFamily: "JetBrains Mono, Courier New", fontSize: 18, fontWeight: 600, letterSpacing: 1 }

rounded:
  sm: 8
  md: 12
  lg: 20     # Karten, Sheets
  xl: 28     # große Fotos, Editor-Leinwand
  full: 9999 # Buttons, Chips, Badges

spacing:
  xxs: 4
  xs: 8
  sm: 12
  md: 16     # Standard-Seitenrand auf dem Handy
  lg: 24
  xl: 32
  xxl: 48

components:
  button-primary:   { backgroundColor: "{colors.primary}", textColor: "{colors.on-primary}", typography: "{typography.button}", rounded: "{rounded.full}", height: 52 }
  button-secondary: { backgroundColor: "{colors.surface-sunk}", textColor: "{colors.ink}", typography: "{typography.button}", rounded: "{rounded.full}", height: 48 }
  button-ghost:     { backgroundColor: transparent, textColor: "{colors.ink-soft}", typography: "{typography.button}", rounded: "{rounded.full}" }
  card:             { backgroundColor: "{colors.surface}", borderColor: "{colors.hairline}", rounded: "{rounded.lg}", padding: "{spacing.md}" }
  chip-top:         { backgroundColor: "{colors.top}", textColor: "{colors.on-top}", typography: "{typography.caption}", rounded: "{rounded.full}" }
  chip-flop:        { backgroundColor: "{colors.flop}", textColor: "{colors.on-flop}", typography: "{typography.caption}", rounded: "{rounded.full}" }
  badge-story:      { backgroundColor: "{colors.story}", textColor: "{colors.on-story}", typography: "{typography.caption}", rounded: "{rounded.full}" }
  badge-persistent: { backgroundColor: "{colors.persistent}", textColor: "{colors.on-persistent}", typography: "{typography.caption}", rounded: "{rounded.full}" }
  info-bar:         { backgroundColor: "{colors.surface-sunk}", textColor: "{colors.ink-soft}", typography: "{typography.body-sm}", rounded: "{rounded.md}" }
  text-input:       { backgroundColor: "{colors.surface}", borderColor: "{colors.hairline}", textColor: "{colors.ink}", typography: "{typography.body}", rounded: "{rounded.md}", height: 48 }
  secret-box:       { backgroundColor: "{colors.surface}", borderColor: "{colors.primary}", typography: "{typography.code}", rounded: "{rounded.lg}" }
---

# Bubble – Design-Guidelines

Diese Datei ist verbindlich für jede Oberfläche von Bubble. Menschen und KI-Agenten bauen danach. Die Vorlagen in [docs/design/references](docs/design/references/README.md) sind nur Inspiration.

## 1. Visuelle Stimmung

Bubble fühlt sich an wie **ein Tagebuch, das man mit seinen Liebsten teilt**: warm, ruhig und persönlich. Es ist kein Social-Media-Feed, der um Aufmerksamkeit buhlt.

- **Warmes Papier statt Weiß.** Jede Seite steht auf `canvas`. Reines Weiß (`surface`) ist für Karten und Eingabefelder reserviert, die sich davon abheben.
- **Fotos tragen die Gestaltung.** Die Oberfläche tritt zurück, wenn ein Bild da ist.
- **Ruhe in der App, Spiel im Editor.** Die gesättigten Editor-Farben gibt es nur auf der Story-Leinwand. Die App-Oberfläche bleibt in Papier- und Erdtönen.
- **Vertrauen durch Klarheit.** Datenschutz zeigt sich durch klare, ruhige Hinweise, nicht durch Schlösser-Icons überall.

## 2. Farben und ihre Rollen

| Rolle | Token | Regel |
|---|---|---|
| Hauptaktion | `primary` | **Genau eine** pro Ansicht: „Teilen“, „Los geht's“, „Backup-Code erstellen“. Nie für Links, Icons oder Deko. |
| Hintergrund | `canvas` | Immer. Nie Weiß als Seitenhintergrund. |
| Karten, Felder | `surface` + `hairline` | Rahmen 1 px, keine harten Schatten. |
| Fließtext | `ink`, `ink-soft` | `muted` nur für Metadaten wie Zeit, Server oder Hinweise. |
| Top / Flop | `top` / `flop` | Bewusst **nicht** Grün/Rot: Ein schlechter Tag ist kein Fehler. |
| Lebensdauer | `story` / `persistent` | Badges an jedem Post und in der Bubble-Kopfzeile. |
| Fehler | `error` | Nur für echte Fehler, nie für „Flop“. |

**Kontrast:** Text erreicht mindestens 4,5:1, große Schrift und Icons mindestens 3:1. Alle Text-Token oben erfüllen das auf `canvas` und `surface`.

**Dunkelmodus:** `dark-*`-Token, warm statt blaugrau. `dark-primary` ist heller und trägt dunklen Text (`dark-on-primary`). Fotos werden nicht abgedunkelt.

## 3. Typografie

- **Fraunces** (Serif) nur für Display und Titel: Begrüßung, Bubble-Name und Leerzustände. Sie gibt den „Tagebuch“-Charakter.
- **Inter** für alles andere. Die Gewichte bleiben moderat (400/500/600), nie 800+.
- **Backup-Codes** werden in Monospace angezeigt, groß und mit Abstand zwischen den Fünfergruppen.
- Systemschriftgrößen bzw. Dynamic Type werden respektiert. Layouts müssen bei 200 % funktionieren.
- **Schriften liegen in der App.** Kein Laden von Google Fonts oder anderen CDNs, denn das würde die IP der Nutzer an Dritte senden.

## 4. Komponenten

**Buttons.** Sie sind pillenförmig (`rounded.full`) und mindestens 48 pt hoch. `button-primary` gibt es einmal pro Ansicht, Nebenaktionen nutzen `button-secondary` oder `button-ghost`. Deaktivierte Buttons erklären, warum sie deaktiviert sind, z. B. „✅ Heute schon geteilt“ statt eines grauen „Teilen“.

**Post-Karte (Feed).**
```
┌──────────────────────────────────┐
│ Anna                   [👍 Top]  │  heading + chip-top/flop
│ ┌──────────────────────────────┐ │
│ │            Foto              │ │  rounded.xl, volle Kartenbreite
│ └──────────────────────────────┘ │
│ Sonnenaufgang am See             │  body
│ heute, 07:12      [⏳ noch 14 h] │  caption muted + badge
└──────────────────────────────────┘
```
Das Foto steht vor dem Text. Der eigene Post ist mit „(du)“ markiert, nicht farbig hervorgehoben.

**Bubble-Zeile (Übersicht).** Bubble-Name in `title`, darunter Lebensdauer-Badge und Server in `caption muted`. Später kommt ein Punkt dazu, wenn es Neues gibt, aber **keine Zähler** für Ungelesenes.

**Info-Leiste.** `info-bar` für Kontext wie „Neue Posts: ⏳ 24 Stunden · Ändern“. Sie hat höchstens eine Zeile Text und eine Ghost-Aktion.

**Geheimnis-Box.** `secret-box` für Backup-Codes und, nach einer Warnung, Einladungslinks. Sie ist umrandet in `primary` und enthält immer: den Wert, eine Kopieren-Aktion und einen Satz, wo man ihn sicher aufbewahrt (Passwortmanager).

**Leerzustände.** Ein Satz in Fraunces und ein Satz in Inter, darunter die eine passende Aktion. Kein Stock-Foto.

## 5. Layout

- Der Seitenrand beträgt auf dem Handy 16 pt (`spacing.md`), der Abstand zwischen Karten 12 pt.
- Eine Spalte auf dem Handy. Auf Tablet und Desktop ist der Feed höchstens 560 pt breit und zentriert, damit er nicht zur Zeitung wird.
- Die Hauptaktion sitzt unten und ist mit dem Daumen erreichbar, nicht in der Toolbar versteckt.
- Großzügig mit Luft umgehen: leerer Papiergrund ist Teil der Ruhe.

## 6. Tiefe und Ebenen

- Elevation entsteht über `surface` auf `canvas` plus `hairline`, nicht über Schatten.
- Ein Schatten ist nur für schwebende Elemente erlaubt (Sheets, Editor-Werkzeugleiste): weich, warm und mit niedriger Deckkraft, z. B. `0 8 24 rgba(57,34,30,0.08)`.
- Modale Dialoge sind die Ausnahme. Für Auswahl wie Lebensdauer oder Mood nutzen wir Bottom Sheets.

## 7. Bubble-spezifische Regeln

**Datenschutz sichtbar, aber leise**
- Jeder Post zeigt seine Lebensdauer (⏳/📌). Vor dem Teilen steht im Compose-Screen, was mit dem Post passiert.
- Beim Foto-Hinzufügen steht der Hinweis „Standort- und Kameradaten werden entfernt“, in `caption muted`.
- Vor dem Teilen eines Einladungslinks kommt **immer** die Erklärung, dass der Link den Schlüssel enthält.
- Server-Adressen und Fehlerursachen werden konkret benannt, z. B. „192.168.1.20:5080 ist nicht erreichbar“.

**Keine Aufmerksamkeitsfallen**
- Kein endloser Feed, keine Streaks, keine Like-Zähler, keine „X hat noch nicht gepostet“-Erinnerungen.
- Reaktionen (später) sind Emojis ohne öffentliche Zählung.
- Benachrichtigungen kommen höchstens einmal täglich und sind inhaltsleer, z. B. „Neue Highlights in deinen Bubbles“.

**Sprache**
- Wir duzen, sind warm und kurz. Wir sagen „Highlight“, „Bubble“ und „teilen“, nicht „Content“, „Gruppe“ oder „posten“.
- Fehler sagen, was passiert ist und was man tun kann, ohne technische Begriffe. Die Ausnahme ist die Server-Adresse.
- Ein Flop-Tag wird nie kommentiert oder bewertet.

## 8. Do's and Don'ts

### Do
- `canvas` als Hintergrund jeder Seite verwenden.
- Genau einen `primary`-Button pro Ansicht setzen.
- Fotos groß zeigen, mit `rounded.xl`.
- Lebensdauer-Badge an jedem Post anzeigen.
- Erklären, warum etwas deaktiviert ist.
- Touch-Ziele mindestens 44×44 pt groß machen.
- Den Dunkelmodus mit den `dark-*`-Token umsetzen.

### Don't
- Kein reines Weiß als Seitenhintergrund.
- `primary` nicht für Links, Icons oder Dekoration verwenden.
- Top/Flop nicht in Grün/Rot einfärben.
- Editor-Farben nicht in der App-Oberfläche einsetzen.
- Keine schweren oder harten Schatten.
- Keine Schriften oder Bilder von externen Servern laden.
- Keine Zähler, Streaks oder Gamification einbauen.

## 9. Responsive Verhalten

| Breite | Verhalten |
|---|---|
| < 600 pt | eine Spalte, Hauptaktion unten fixiert |
| 600–1024 pt | Feed zentriert (max. 560 pt), Bubble-Liste als Seitenleiste möglich |
| > 1024 pt (Desktop) | Bubble-Liste links (320 pt), Feed rechts, Editor als großes Sheet |

Die Story-Leinwand hat immer das Format **9:16** und wird skaliert, nie beschnitten.

## 10. Story-Editor

- Die Leinwand ist 1080×1920 (9:16) mit `rounded.xl` in der Vorschau. Der Export ist ein flaches JPEG.
- Hintergründe nutzen `editor-*`-Farben oder Verläufe zwischen zwei benachbarten Editor-Farben.
- Vorlagen wechseln die Farben ab (pink → teal → lavender → peach → ochre) und wiederholen keine Farbe direkt.
- Text auf der Leinwand ist Fraunces oder Inter. Zusätzlich gibt es höchstens eine verspielte Handschrift, die in der App mitgeliefert wird.
- Die Werkzeugleiste ist ruhige App-Oberfläche (`surface`, schwebend). Bunt ist nur, was auf der Leinwand liegt.

## 11. Umsetzung in .NET MAUI

| Token | MAUI-Ressource (`Resources/Styles/Colors.xaml`) |
|---|---|
| `primary` / `dark-primary` | `Primary` / `PrimaryDark` |
| `canvas` / `dark-canvas` | `Canvas` / `CanvasDark` → `ContentPage.BackgroundColor` |
| `surface`, `surface-sunk`, `hairline` | `Surface`, `SurfaceSunk`, `Hairline` |
| `ink`, `ink-soft`, `muted` | `Ink`, `InkSoft`, `Muted` |
| `top`/`on-top`, `flop`/`on-flop` | `Top`, `OnTop`, `Flop`, `OnFlop` |
| `story`/`persistent` (+ `on-*`) | `Story`, `OnStory`, `Persistent`, `OnPersistent` |

- Farben laufen über `AppThemeBinding` (hell/dunkel), Komponenten über benannte Styles in `Styles.xaml` (`PrimaryButton`, `SecondaryButton`, `Card`, `ChipTop`, `BadgeStory` …).
- Schriften: `Fraunces-Variable.ttf` und `Inter-Variable.ttf` in `Resources/Fonts`. Bis sie eingebunden sind, ist OpenSans der Ersatz.

## 12. Prompt-Hilfe für KI-Agenten

> Baue die Oberfläche nach `DESIGN.md`: warmer Papiergrund (`canvas` #F7F1E8), weiße Karten mit 1-px-Rahmen und 20-pt-Radius, genau ein violetter Pillen-Button (#5B47E0) pro Ansicht. Fraunces für Titel, Inter für Text. Fotos groß und abgerundet. Jeder Post zeigt Top/Flop-Chip (Sonne/Regenwolke, nicht grün/rot) und Lebensdauer-Badge (⏳ Sand / 📌 Salbei). Keine Zähler, keine Streaks, keine externen Ressourcen. Deutsch, Du-Form, warm und kurz.
