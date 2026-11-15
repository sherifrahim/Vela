# Reference: what the original (lumabar) does

Compiled 2026-11-15 from lumabar.xyz/widgets, /changelog, /docs and its public README
(screenshots in `reference/lumabar/assets/`). X (@lumabar_xyz) wasn't readable by automated fetch (HTTP 402).
This is a feature inventory in our own words, our parity target. Their latest version at the time: 1.2.19 (Nov 12, 2026).

## Dock
- One bar on the bottom (default) or top edge; drag to the top half of the screen to snap to top; panels then open downward.
- Main screen only (as of 1.2.14; earlier "all displays" / "follow pointer" modes were removed).
- Hides the Windows taskbar (hidden or auto-hide) while running; restores it on exit.
- Hides and stops drawing under fullscreen games/videos.
- Auto-hide dock option (slides away, returns at the edge); "show on desktop" keeps it up on an empty desktop;
  Win key brings an auto-hidden dock up with Start.
- Sizes S/M/L (later draggable continuously), distance from edge, spacing (≤ 24 px), padding,
  full-width variant, narrow-able by dragging its ends, lock dock (widgets move only while the picker is open).
- Mac-style magnification with a size slider; app icon size S/M/L; app cards or icons only.
- Styles: Black, White, Liquid Glass, iOS Glass, Midnight; Clear style; widget tiles black/white/frosted;
  accent colour (optionally from album art). Liquid Glass refracts what moves behind it, rests when nothing moves.

## Apps (taskbar part)
- Start button (right-click = Windows quick menu), pinned apps + open windows, running dots, attention highlight,
  app badges (unread counts from Telegram, Discord… via notifications), live window previews on hover,
  jump lists, pin/unpin, end task for hung apps, close, app groups (drop app on app), folders/stacks,
  pin any file/folder by dragging it in, drag files over an app to bring it forward, Mac-style Trash,
  Steam game icons, Win+T keyboard navigation.

## Widget picker & menus
- Right-click dock → Widgets… / Settings. Picker: category sidebar + search, live previews, checkmarks on
  widgets already in the bar, click to add/remove; minus badges on the dock while open; drag to reorder.
- Cards open over their widget and slide out of the bar.

## Widgets (≈109 tiles in 13 categories) — highlights
- **Clocks:** Clock, Flip Clock, World Clocks, 7 Days, World Time, Today, Date, Day Number, Analog Minimal, Segment Clock, Clock & Weather.
- **Productivity:** Day Timeline, Screen Time, Pomodoro, Timer, Stopwatch, Todos, Notes, Calendar (Google/Outlook/iCloud), WhatsApp (from notifications), Currency (152 currencies).
- **System:** Top CPU, Top RAM, Tray, Language, Temperature, CPU, RAM, Network, Battery (+ Ring, Core, Dots, Gauge), Quick Toggles (Wi-Fi, BT, hotspot, airplane, Ethernet, energy saver, DND), Brightness, Theme Toggle, Do Not Disturb, Notifications, Show Desktop, Keyboard, Month/Day/Year Progress.
- **Media & Audio:** Now Playing (+ compact, mini, bar), Audio Output (with per-app mixer), Microphone.
- **AI:** AI Limits (two providers), Claude Usage, Claude Plan Usage, ChatGPT/Codex Usage, Gemini, Grok, DeepSeek balance, GLM, OpenCode (+ plan), Cursor.
- **Finance:** Total Revenue, Polar, Stripe, Dodo, Shopify, Lemon Squeezy, Paddle, Gumroad (each + 14-day chart).
- **Analytics:** Search Console, DataFast, Plausible, Dub. **Developer:** Resend, Linear, Supabase.
- **Online & Mail:** Wallpaper Gallery (Unsplash), Gmail, GitHub Activity, GitHub, GitHub Overview, LeetCode.
- **Social:** X, TikTok, YouTube, Instagram. **Stocks:** Stock Chart, Watchlist (Finnhub), Crypto (Binance).
- **Weather:** Weather, Details, Today & Forecast, Status, Hourly, Sunrise & Sunset.

## Connectors (how the original authenticates)
- Claude: reads Claude Code's saved sign-in (Pro/Max); falls back to counting tokens from local logs. Never writes Claude Code's files.
- ChatGPT: reads the Codex CLI sign-in (`codex login`); shows Codex 5-hour + weekly windows.
- Gemini → Antigravity sign-in; Grok → SuperGrok sign-in; Cursor → signed-in app ≥ 3.27; OpenCode → local history.
- DeepSeek, GLM, Stripe (restricted key: Balance + Balance transaction sources = Read), Polar/Dodo/Shopify/…, GitHub (classic PAT, `read:user` + `notifications`), YouTube, Instagram, DataFast, Plausible, Dub, Resend, Linear, Supabase → pasted keys, stored only on the PC.
- Gmail: user's own Google Cloud OAuth desktop client (read-only scope, testing mode → re-login every 7 days).
- Right-click a connected tile → Logout.

## Notifications & automations
- Notifications tile opens the Notification Center; Do Not Disturb tile/toggle; app badges; attention highlight.
- Automations: pause music before meetings, warn before an AI limit runs out, remind before renewals,
  turn review requests into todos; quiet hours. Alert sounds for timer/focus/calendar/todo.

## Keyboard
- Alt+T opens Todos from anywhere; Win+T shows open apps (arrows/Tab/Enter); Win key with auto-hidden dock.

## Privacy model
- No account, no server, no telemetry; each widget talks directly to its service; keys stay on the PC.
