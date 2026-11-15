# Roadmap / parity checklist

`[x]` done & verified on the owner's PC · `[~]` implemented, not fully verified · `[ ]` todo.
Pick from the top of each section. Keep ids/names in sync with `Widgets/WidgetRegistry.cs`.

## P0 — core dock (must stay solid)
- [x] Native WPF dock on bottom edge, primary monitor, DPI-aware (175 % verified)
- [x] Appbar space reservation; maximized windows stop above the dock
- [x] Hide Windows taskbar + guaranteed restore (marker, `--quit`, `--restore-taskbar`)
- [x] Five styles, accent colours, S/M/L, widget-tile override, wallpaper-based glass
- [x] Widget picker (categories, search, live previews, checkmarks), edit-mode minus badges
- [x] Settings window (appearance, dock, weather, connectors, privacy, about)
- [~] Top edge + drag-to-snap (code done, not yet tested by hand)
- [~] Drag to reorder widgets while picker is open (code done, not tested by hand)
- [~] Fullscreen hide/restore (code done; test with a game / F11 video)
- [~] Cards slide out over their widget (seen working in code path; verify each card visually)
- [ ] Tooltips should open *below* when the dock is on top (ToolTip style uses Placement=Top)
- [ ] Auto-hide dock mode (slide away, reveal at edge, "show on desktop", Win key reveal)
- [ ] Mac-style magnification (hover scale exists per icon; add neighbour falloff + size slider)
- [ ] Narrow / full-width bar variants; padding setting
- [ ] Lock dock setting

## P1 — apps (taskbar parity)
- [x] Start button (click = Start, right-click = quick menu shortcuts), pinned apps imported from taskbar
- [x] Running windows grouped by AUMID/exe, running dots, multi-window cycling, attention (flash) dot
- [x] Context menu: window list, new window, pin/unpin, close
- [ ] Live window previews on hover (DwmRegisterThumbnail into a card)
- [ ] Jump lists (ICustomDestinationList / automatic destinations)
- [ ] End task for hung windows (IsHungAppWindow)
- [ ] App badges (unread counts) — needs notification access; investigate `UserNotificationListener` with a sparse package identity
- [ ] Drag files/folders onto the dock to pin; drag files over an app to bring it forward
- [ ] App groups / folder stacks; Trash on the dock
- [ ] Win+T keyboard navigation (RegisterHotKey + focusable app strip)
- [ ] Tray icons grid (needs Shell_NotifyIcon hosting or reading the tray toolbar — research)

## P1 — widgets
Done (41): clock, clock.digital, date, dayprogress, monthprogress, yearprogress, screentime, pomodoro, timer,
stopwatch, todos, notes, currency, sysstats, cpu, ram, network, battery, battery.dots, battery.ring,
quicktoggles, brightness, themetoggle, notifications, showdesktop, taskmanager, apps, storage, recyclebin,
nowplaying, nowplaying.mini, volume, microphone, claude, codex, ailimits, stripe, crypto, weather, weather.sun, github.

Todo, roughly by value:
- [ ] Calendar (next event) — Outlook/Windows calendar via `Windows.ApplicationModel.Appointments` first, Google later
- [ ] Do Not Disturb toggle (Focus Assist: `WNF`/quiet hours APIs — research), Keyboard (osk.exe), Language (keyboard layout switch)
- [ ] Top CPU / Top RAM tiles (per-process CPU% needs two samples; reuse SystemMonitor ticks)
- [ ] Temperature (WMI MSAcpi_ThermalZoneTemperature needs admin → research LibreHardwareMonitor-style alternatives)
- [ ] Flip clock, analog clock, world clocks, segment clock, "Today", clock & weather, 7 days
- [ ] Now Playing compact + bar variants; per-app volume mixer card (IAudioSessionManager2)
- [ ] Quick toggles: hotspot, airplane, energy saver, Ethernet status, user-selectable toggles
- [ ] Weather details / hourly / today & forecast / status tiles (service already has the data)
- [ ] Brightness for external monitors (DDC/CI via dxva2 `SetMonitorBrightness`)
- [ ] Search / Commands launcher, Recently opened, Screenshot capture
- [ ] AI: Gemini (Antigravity), Grok, DeepSeek balance (key), GLM (key), OpenCode (local), Cursor
- [ ] Finance: Polar, Dodo, Shopify, Lemon Squeezy, Paddle, Gumroad, Total Revenue (+ charts)
- [ ] GitHub activity grid / overview, Gmail (own OAuth client, loopback redirect), LeetCode
- [ ] Analytics & developer: Plausible, DataFast, Dub, Search Console, Resend, Linear, Supabase
- [ ] Stocks: watchlist + chart (Finnhub key)
- [ ] Social: YouTube (API key), X, TikTok, Instagram
- [ ] Wallpaper gallery

## P2 — glass & animation
- [ ] Live Liquid Glass: Windows.Graphics.Capture of the strip behind the dock → D3D11/Win2D refraction
      shader, rendered only while the capture changes (rest at 0 CPU otherwise). Keep WallpaperGlass as fallback.
- [ ] App launch bounce ✔ exists; add minimize-to-dock "genie"-style hint animation on the icon
- [ ] Mac-style window open/close/minimize animations for *other* apps: only possible by injecting into
      DWM/explorer (that's what Windhawk mods do). Out of scope for Vela core; if wanted, ship as a separate,
      opt-in Windhawk mod in `extras/` and document the risk.

## P2 — notifications & automations
- [ ] Notification listener (badges, WhatsApp tile) — requires package identity (sparse MSIX) → research
- [ ] Automations: AI limit warning toast, timer/pomodoro end toast (currently a system sound), quiet hours
- [ ] Global hotkeys: Alt+T todos, Win+T apps

## Engineering
- [ ] Unit tests (xUnit) for pure logic: Claude/Codex log parsing, Fmt, WallpaperGlass mapping, Stripe summing
- [ ] Settings migration framework (`AppSettings.Version`)
- [ ] App icon (.ico) + version resource; MSIX or Inno Setup installer; auto-start toggle verified
- [ ] Multi-monitor: choose display; per-monitor DPI changes while running
- [ ] Make the `chrome` render mode work (no region, exact-size window, DWM rounded corners) and compare CPU/memory
- [ ] Localisation-safe formatting (currently uses current culture for dates/numbers)
