# Parity with the reference (what it charges for)

Checked 2026-11-17 against lumabar.xyz (home, /widgets, /docs, /changelog, /terms; latest version 1.2.20, Nov 16 2026).

**Pricing model:** one-time $5.99 per PC (shown as $15.99 struck through), no subscription, no free tier and
no trial. Every widget and feature comes with the one license; there is no free/paid split. So "everything it
charges for" = the whole app below.

## Widgets: 101 of 109

| Category (theirs) | Have | Missing |
|---|---|---|
| Clocks (11) | 11 | — |
| Productivity (10) | 10 | — (Calendar shows Google/Outlook/iCloud events through private iCal links) |
| System (23) | 22 | Do Not Disturb (no public API to toggle it). Temperature shows the GPU sensor (WDDM); CPU temperature needs a kernel driver |
| Media & Audio (6) | 6 | — |
| AI (11) | 8 | Gemini (reads the Antigravity sign-in), Grok (SuperGrok has no public usage API), OpenCode Plan. Cursor added 2026-11-24 (browser sign-in) |
| Finance (15) | 15 | — |
| Analytics (4) | 3 | DataFast |
| Developer (3) | 3 | — |
| Online & Mail (6) | 6 | — |
| Social (4) | 1 | X, TikTok, Instagram (their APIs need paid / reviewed developer apps) |
| Apps & Files (7) | 7 | — |
| Weather (6) | 6 | — |
| Stocks (3) | 3 | — (Stock Chart uses Yahoo Finance's public chart data, no key) |

Account widgets added 2026-11-17 (`Connectors/AccountConnectors.cs`, `Widgets/AccountWidgets.cs`): Polar, Lemon
Squeezy, Paddle, Gumroad, Shopify, Dodo (+ chart variants), Total Revenue, DeepSeek, Plausible, Dub, Linear,
Supabase, Resend, YouTube, LeetCode, GitHub Activity, Stock Watchlist (Finnhub). Endpoints verified to exist
(401/403 without a key); LeetCode verified end to end. **None verified with a real key yet.**

## Features

| Reference feature | Vela |
|---|---|
| Styles, sizes, top/bottom, full width, auto-hide, taskbar hiding | ✔ |
| Live blur under the dock | ✔ (Acrylic) + real Liquid Glass refraction (GPU shader over live window capture) |
| Widget picker, edit mode, drag to reorder, lock dock | ✔ |
| Apps: pins, previews, jump lists, end task, attention, open indicator (dot/large/line) | ✔ |
| Win+1…0 open dock apps | ✘ (Explorer owns those keys) |
| App badges / WhatsApp / Notification counts | ✔ (Windows notification listener; works without packaging) |
| Automations (pause music before meetings, AI-limit / renewal warnings, quiet hours) | ✘ |
| Calendar accounts (Google, Outlook, iCloud) | ✔ via private iCal links (no OAuth needed) |
| Screen time per app | ✔ (per site ✘) |
| One-click in-app updates, installer | ✘ |

## Vela has that the reference doesn't

Vela Island, a dock per display with per-display adaptive light/dark, Control Centre (Wi-Fi/Bluetooth device
lists), stacks, Downloads stack, Terminal launcher + Alt+T admin PowerShell, Task Manager / System Monitor
tiles, per-app sound mixer and output switch, macOS magnification, open source and free.

## Sign-in widgets (2026-11-17)

- **Gmail, Search Console** (`Connectors/GoogleConnectors.cs`): like the reference, the user brings their own Google
  Cloud OAuth client ("Desktop app"), pastes "client-id client-secret", then clicks *Sign in with Google*. PKCE +
  a one-shot listener on 127.0.0.1; read-only scopes; refresh token in Secrets. In Google's testing mode the sign-in
  lasts 7 days, then the tile asks again. Loopback parsing unit-tested; **never run against a real Google account.**
- **GLM Coding Plan** (`Connectors/AiConnectors.cs`): Z.ai quota endpoint (`/api/monitor/usage/quota/limit`, also
  open.bigmodel.cn with a "cn " prefix). Endpoint verified; response parsing written from public descriptions, not a real key.
- **OpenCode**: summed from OpenCode's local message files; parser unit-tested; OpenCode isn't installed on the dev PC.
