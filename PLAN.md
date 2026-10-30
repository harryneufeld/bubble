# Bubble – Plan

## Goal
An app for sharing highlights in private groups ("bubbles") – with an emoji for the feeling, as text or as an image. Privacy comes first: end-to-end encrypted and data-minimal.

## Core features
- **Bubbles:** small private groups, joined via an invitation link
- **Share highlights** as often as you like, with a feeling as an emoji ("How are you feeling?")
- **Heart bubble and comments** under every highlight; both disappear with the post
- **Members:** anyone can leave a bubble; the person who created it can remove members (the bubble moves to a new key)
- **Editor like Canva:** story canvas (9:16) with photos, text, stickers and templates; the finished image is shared
- **Lifetime configurable per bubble:** ⏳ 24h (like stories) or 📌 permanent
  - every post shows a badge with its lifetime
  - a change only applies to new posts, and the bubble is informed

## Privacy
- **End-to-end encryption** (ChaCha20-Poly1305): one key per bubble, rotated when someone is removed
- **"Blind" relay server** in the EU: only stores encrypted blobs and deletes 24h posts automatically
- **No email and no phone number:** the identity is a key pair on the device
- **Images:** metadata (EXIF/GPS) is removed before upload, the image is compressed and encrypted
- **Content-free push notifications**, no IP logs, no external assets

## New device / recovery
An encrypted key backup sits on the server. It contains the identity and all bubble keys and can be opened in two ways:
1. **Passkey** (WebAuthn PRF), synced via iCloud or Google – the default path
2. **Backup code**, with a hint to save it in a password manager – the alternative

Fallback: friends invite you into the bubble again.

## Stack
.NET 10 · .NET MAUI (app) · ASP.NET Core + SQLite (relay) · BouncyCastle (ChaCha20-Poly1305, Ed25519, X25519, Argon2id) · SkiaSharp (image processing, editor later)

## Roadmap
1. **MVP:** bubbles, text and photo posts, end-to-end encryption, 24h/permanent, passkey and code backup
2. **Editor:** canvas, templates, stickers
3. **Comfort:** emoji reactions, push, multi-device support
4. **Hardening:** MLS, metadata protection, option to self-host
