# Parity with the reference (what it charges for)

Checked 2026-11-17 against lumabar.xyz (home, /widgets, /docs, /changelog, /terms; latest version 1.2.20, Nov 16 2026).

**Pricing model:** one-time $5.99 per PC (shown as $15.99 struck through), no subscription, no free tier and
no trial. Every widget and feature comes with the one license; there is no free/paid split. So "everything it
charges for" = the whole app below.

## Widgets: 81 of 109

| Category (theirs) | Have | Missing |
|---|---|---|
| Clocks (11) | 10 | World Time |
| Productivity (10) | 8 | Day Timeline, WhatsApp (needs notification access) · Calendar has no Google/Outlook/iCloud events yet |
| System (23) | 20 | Tray, Temperature, Do Not Disturb |
| Media & Audio (6) | 4 | Now Playing compact, Now Playing Bar |
| AI (11) | 5 | Gemini (Antigravity sign-in), Grok, GLM, OpenCode, OpenCode Plan, Cursor |
| Finance (15) | 14 | Stripe Chart |
| Analytics (4) | 2 | Search Console (Google OAuth), DataFast |
| Developer (3) | 3 | — |
| Online & Mail (6) | 3 | Gmail (own Google OAuth client), GitHub Overview, Wallpaper Gallery (Unsplash) |
| Social (4) | 1 | X, TikTok, Instagram (their APIs need paid / reviewed developer apps) |
| Apps & Files (7) | 3 | Recently Opened, Search, Commands, Screenshot Capture |
| Weather (6) | 6 | — |
| Stocks (3) | 2 | Stock Chart (Finnhub candles are a paid endpoint; needs another source) |

Account widgets added 2026-11-17 (`Connectors/AccountConnectors.cs`, `Widgets/AccountWidgets.cs`): Polar, Lemon
Squeezy, Paddle, Gumroad, Shopify, Dodo (+ chart variants), Total Revenue, DeepSeek, Plausible, Dub, Linear,
Supabase, Resend, YouTube, LeetCode, GitHub Activity, Stock Watchlist (Finnhub). Endpoints verified to exist
(401/403 without a key); LeetCode verified end to end. **None verified with a real key yet.**

## Features

| Reference feature | Vela |
|---|---|
| Styles, sizes, top/bottom, full width, auto-hide, taskbar hiding | ✔ |
| Live blur under the dock | ✔ (Acrylic); real Liquid Glass *refraction* ✘ |
| Widget picker, edit mode, drag to reorder, lock dock | ✔ |
| Apps: pins, previews, jump lists, end task, attention, open indicator (dot/large/line) | ✔ |
| Win+1…0 open dock apps | ✘ (Explorer owns those keys) |
| App badges / WhatsApp / Notification counts | ✘ needs notification access (package identity) |
| Automations (pause music before meetings, AI-limit / renewal warnings, quiet hours) | ✘ |
| Calendar accounts (Google, Outlook, iCloud) | ✘ |
| Screen time per app | ✔ (per site ✘) |
| One-click in-app updates, installer | ✘ |

## Vela has that the reference doesn't

Vela Island, a dock per display with per-display adaptive light/dark, Control Centre (Wi-Fi/Bluetooth device
lists), stacks, Downloads stack, Terminal launcher + Alt+T admin PowerShell, Task Manager / System Monitor
tiles, per-app sound mixer and output switch, macOS magnification, open source and free.
