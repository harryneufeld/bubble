---
version: 2
name: Bubble
description: A dark, vivid social app for small private circles. Deep black-violet as the base, photos and glowing gradients (pink → violet → cyan, like the shimmer of a soap bubble) as the visual language. Story bar, full-screen stories and photo cards like in social apps – but without streaks, leaderboards or endless feeds.

colors:
  # Surfaces (dark)
  canvas: "#0B0A10"
  surface: "#17151F"
  surface-raised: "#221F2C"
  hairline: "#2E2A3A"

  # Text
  ink: "#F5F3FA"
  ink-soft: "#C9C4D6"     # >= 4.5:1 on canvas and surface
  muted: "#8F89A0"        # metadata only, >= 4.5:1 on canvas

  # Shimmer – accent gradient for rings, covers, decoration
  pink: "#FF4F9A"
  violet: "#8B5CF6"
  cyan: "#22D3EE"
  accent: "#B9A2FF"       # text links and ghost buttons

  # Primary action: gradient cta-start → cta-end, white text (>= 4.5:1)
  cta-start: "#D9246F"
  cta-end: "#6D3CF5"
  on-cta: "#FFFFFF"

  # Semantics
  error: "#FF5A6E"
  success: "#4ADE80"

typography:
  # Display: Fraunces (wordmark only) · UI: Inter (SIL OFL). Both ship as TTF inside the app.
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
  md: 16     # page margin
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
  avatar:           { shape: circle, fallback: "initials on a gradient, stable per name" }
  story-ring:       { new: "gradient(pink → violet → cyan)", seen: "#3A3548" }
---

# Bubble – Design Guidelines

This file is binding for every Bubble surface. People and AI agents build against it. The templates in [docs/design/references](docs/design/references/README.md) are inspiration only.

## 1. Mood

Bubble feels like **the story bar of a social app, only for your closest people**: dark, vivid, photo-heavy. The interface steps back; photos and glowing gradients carry the design.

- **Dark base.** The app is always dark (`UserAppTheme = Dark`). Not pure black, but `canvas` with a hint of violet.
- **Shimmer instead of flat color.** Pink → violet → cyan appears in a few places: story rings, covers, the main button, decoration. Never as a text color or as a background for running text.
- **Photos first.** Cards run edge to edge. Text posts get a glowing gradient so they feel like photos.
- **Social, but calm.** See section 7: no counters, no streaks, no pull.

## 2. Colors and their roles

| Role | Token | Rule |
|---|---|---|
| Main action | `cta-start → cta-end` | **Exactly one** per view ("Share", "Get started", "Join"). A round `+` icon button in the same gradient may also appear in headers. |
| Base | `canvas` | Page background. |
| Cards, fields | `surface` (+ `hairline` for fields) | No borders around cards, just surface on base. |
| Emphasis | `surface-raised` | Secondary buttons, chips, icon buttons. |
| Running text | `ink`, `ink-soft` | `muted` only for metadata like time and server. |
| Links, ghost actions | `accent` | Not for running text. |
| Shimmer | `pink`, `violet`, `cyan` | Rings, covers, decoration (see above). |
| Feeling | `surface-raised` | Neutral pill with emoji and word ("😍 In love"). No judgment colors: a bad day is not an error. |
| Heart | `pink` | Only the set heart in the heart bubble. |
| Error | `error` | Only for real errors, never for a feeling. |

**Contrast:** text at least 4.5:1, large type and icons at least 3:1. White text on gradients uses the darker shades (`cta-*`, avatar and post gradients). Text on photos always sits on a shadow gradient (`PhotoShade`).

## 3. Typography

- **Inter** for everything, weights 400/500/600/700. Headlines are bold and tightly set (`hero`, `title`).
- **Fraunces** (serif) only for the "bubble" wordmark. It stays as the recognizable mark.
- **Backup and invitation codes** in monospace, large, with letter spacing.
- Dynamic Type and system font sizes are respected; layouts must work at 200 %.
- **Fonts ship inside the app** (`Resources/Fonts`, licenses in `docs/licenses`). Never load from a CDN – that would send users' IPs to third parties.
- Static TTFs, because MAUI cannot drive the axes of variable fonts. Aliases: `Display`, `Body`, `BodyMedium`, `BodySemibold`, `BodyBold`.

## 4. Components

**Navigation.** Back closes pages one after another; from another tab it goes back to "Today". On "Today", back does not exit the app straight away. Three tabs at the bottom: **Today**, **Bubbles**, **Profile**, with line icons (2 px, `ic_*.svg`). Selected = `ink`, otherwise `muted`. Detail pages (bubble, invite, join, share, backup) hide the tab bar.

**Avatar.** Round. Without a photo: initials on a stable gradient per name. Optionally with a story ring (`new` = gradient, `seen` = subtle) and a plus badge ("You" in the story bar).

**Story bar (Today).** Horizontal row: first "You" (share), then one bubble per circle with highlights from the last 24 h. Unseen ones first with a gradient ring, seen ones subtle. Tapping opens the full-screen view.

**Story view.** Full screen on black: progress bars at the top (6 s per story), sender and close button, feeling pill and caption at the bottom on a shadow. Tap right = next, left = back. Starts at the first unseen story.

**Post card.**
```
┌──────────────────────────────────┐
│ (Avatar) Anna      [😍 In love]  │  Header: name, "Bubble · 2 h ago", feeling
│ ┌──────────────────────────────┐ │
│ │ Photo, edge to edge [⏳ 14 h]│ │  Glass pill with lifetime
│ └──────────────────────────────┘ │
│ Sunrise at the lake              │
│ (♡) (Mia)(Tom) Mia and Tom   💬 2 │  Heart bubble, who gave a heart, comments
└──────────────────────────────────┘
```
The **heart bubble** is a heart in a soap bubble (outline; set: filled pink) and pops briefly when tapped. On the post page, double-tapping the image also gives a heart.

**Post page.** Tapping a card opens just that post, large, with the comments below and the input pinned at the bottom. Comments are round speech bubbles with avatar, name and time; hearts and comments disappear with the post.
Text-only posts: large white text centered on a glowing gradient, the same height as a photo post.

**Bubble tile (Bubbles).** Two columns. Cover = newest photo or gradient, shadow at the bottom, faces of the members, name, "n people · lifetime". A dot at the top right shows something new – **no counter**.

**Bubble page.** Cover header with name, lifetime and people pills; member row (the creator can tap a member to remove them); "Invite people" and "Lifetime"; "Leave bubble" as a quiet ghost action; the highlights below. Share button at the bottom.

**Secret box.** Frame in the shimmer gradient, always contains: the value, a copy action and a sentence on where to keep it safe.

**Empty states.** Decoration of three gradient bubbles, a headline, one sentence, the one fitting action.

## 5. Layout

- Page margin 16 pt, card spacing 16 pt. One column on phones.
- On tablet and desktop the content column is at most 600 pt wide and centered.
- The main action sits at the bottom, within thumb reach.
- Touch targets at least 44×44 pt.

## 6. Depth

- Depth comes from `surface` on `canvas`, not from shadows.
- On photos, semi-transparent glass pills (`#8C0B0A10`) show lifetime and feeling.
- Pressed buttons scale slightly (0.97). Page changes in onboarding fade in and slide up 24 pt (250 ms). Animations respect "reduce motion".

## 7. Bubble-specific rules

**Adding friends = inviting them into a bubble**
- Invite: QR code (contains the full link), short code (e.g. `7KQ2M-9XH4T`, valid for 24 h) or a shared link. The short code sits on the relay as an encrypted 24h message.
- Join: the camera scans the QR code, or you enter the code or paste the link. Before joining, a preview shows name, lifetime and server.
- Every invitation is preceded by the explanation that the QR code, code and link contain the key.

**Members**
- Only the person who created a bubble can remove members. Removing someone moves the bubble to a new key, and the sheet says so up front, including that earlier highlights stay behind.
- Anyone can leave a bubble. Leaving asks for confirmation and says that coming back needs a new invitation.
- Logging out asks for confirmation and says what is lost if no backup exists.

**Privacy visible, but quiet**
- Every post shows its lifetime (⏳/📌). Before sharing, it says what happens to the post.
- When adding a photo it says "Location and camera data are removed".
- The profile (name, picture, bio) goes only, encrypted, to your own bubbles.
- Server addresses and causes of errors are named concretely, e.g. "192.168.1.20:5080 is not reachable".

**No attention traps**
- No endless feed (only highlights from the bubbles, no infinite loading), no streaks, no leaderboards, no "X hasn't posted yet" hints, no counters for unread items.
- Hearts show **faces and names** ("Mia and Tom"), not a big number. A comment counter ("2 comments") is allowed.
- Emoji reactions (later) follow the same rule.
- Notifications at most once a day and content-free.

**Language**
- We write in English, warm and short. We say "highlight", "bubble" and "share", not "content", "group" or "post".
- Errors say what happened and what you can do. The exception is the server address.
- The app never comments on or rates a bad day. "How are you feeling?" means the person, not a grade.
- Sharing: "How are you feeling?" with 16 emojis and one word; the choice is optional and can be undone by tapping again.

## 8. Do's and Don'ts

### Do
- Use `canvas` as the background of every page, cards in `surface`.
- Put exactly one main button in the gradient per view.
- Show photos large and edge to edge; text on photos only over a shadow.
- Show the lifetime on every post.
- Explain why something is disabled (e.g. "Choose a bubble" on the share button).
- Use line icons from `Resources/Images` (no emojis as navigation).

### Don't
- Don't use the shimmer for running text or for large areas behind low-contrast text.
- Don't mark feelings with judgment colors (green/red).
- Don't load fonts or images from external servers.
- Don't add streaks, leaderboards or gamification; don't show hearts as a number.
- No borders around cards and no hard shadows.

## 9. Responsive behavior

| Width | Behavior |
|---|---|
| < 600 pt | one column, tab bar at the bottom, main action pinned at the bottom |
| ≥ 600 pt | content centered, at most 600 pt wide |

## 10. Story editor (later)

The canvas has a **9:16** format (1080×1920), is scaled and never cropped; export as a flat JPEG. Backgrounds use gradients from `pink`, `violet`, `cyan` and their neighbors. The toolbar is calm app UI (`surface-raised`, floating).

## 11. Implementation in .NET MAUI

- Colors and gradients: `Resources/Styles/Colors.xaml` (`Canvas`, `Surface`, `SurfaceRaised`, `Hairline`, `Ink`, `InkSoft`, `Muted`, `Pink`, `Violet`, `Cyan`, `Accent`, `CtaStart`, `CtaEnd`, brushes `BubbleGradient`, `CtaGradient`, `PhotoShade`).
- Components: named styles in `Resources/Styles/Styles.xaml` (`PrimaryButton`, `SecondaryButton`, `GhostButton`, `IconButton`, `Card`, `Field`, `InfoBar`, `SecretBox`, `GlassPill`, `SoftPill`, `FeelingText`, `HeroText`, `TitleText`, `CaptionText`, `SectionText`, `CodeText`).
- Reusable controls: `Controls/AvatarView` (avatar, ring, plus), `Controls/PostCard` (post card).
- `QRCoder` generates QR codes locally, `ZXing.Net.Maui` scans them (camera only while the scan tab is open).

## 12. Prompt help for AI agents

> Build the UI according to `DESIGN.md`: dark base (`canvas` #0B0A10), cards in `surface` without borders and with a 24 pt radius, exactly one pill button in the pink-violet gradient per view. Inter bold for headlines, Fraunces only for the wordmark. Photos edge to edge and large, text posts on a glowing gradient. Story rings in the pink-violet-cyan gradient. Every post shows the feeling as an emoji pill and the lifetime, with the heart bubble below. No streaks, no external resources. English, warm and short.
