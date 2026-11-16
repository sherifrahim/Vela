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
- [x] Reference proportions: 42 DIP tiles, 3 DIP bar padding, hairline borders, 12/16 radii, reference type scale
- [x] Full-width bar (edge to edge, rounded ends) + "fit content" option; apps centred on screen (`DockRow`)
- [x] Anti-aliased rounded corners (geometry clip instead of a Win32 region) + soft bar shadow
- [x] Maximized windows re-fitted above the dock (`FitMaximizedToWorkArea`)
- [x] Cards open directly over their widget (Left/Top in DIPs) — verified: Bluetooth, Wi-Fi, Control Centre
- [x] Dynamic fit: row scales down (≥ 60 %) when it doesn't fit the display (seen at 86 % on the 1080p screen)
- [x] Dock on every display (`Displays = all`), layouts kept in sync — verified on 2 displays
- [x] macOS magnification with neighbour falloff + size slider — verified
- [x] Control panel (sidebar: General, Appearance, Dock, Widgets, Apps, Weather, Connectors, Privacy, About)
- [~] Drag any widget to reorder (any time) + drag app icons to reorder pins (code done, not hand-tested)
- [~] Widget stacks: "Stack with next widget", scroll to flip, auto-rotate, Unstack (not hand-tested)
- [~] Glass options: Frosted / Liquid Glass / blur strength / glass tiles (not visually checked yet)
- [~] Desktop corner: hover = peek (undocumented DWM call), click = show desktop (peek not verified)
- [~] Top edge + drag-to-snap (code done, not yet tested by hand)
- [x] Fullscreen hide/restore per display — verified (Unreal Engine fullscreen on display 1 hid only that dock)
- [ ] Tooltips should open *below* when the dock is on top (ToolTip style uses Placement=Top)
- [~] Auto-hide: off / smart (window overlaps) / always, edge trigger (code done; not hand-tested — owner was in a fullscreen app)
- [~] Adaptive theme: each display's dock matches its screen (colours + light/dark) — verified dark on display 2
- [~] Live drag-and-drop: dragged item follows the pointer, neighbours glide (code done, not hand-tested)
- [ ] Magnified icons may grow above the bar (needs a taller transparent window band + clip changes)
- [ ] Per-display app filtering ("show only windows on this display") for secondary docks
- [ ] Lock dock setting

## P1 — apps (taskbar parity)
- [x] Start button (click = Start, right-click = quick menu shortcuts), pinned apps imported from taskbar
- [x] Running windows grouped by AUMID/exe, running dots, multi-window cycling, attention (flash) dot
- [x] Context menu: window list, new window, pin/unpin, close
- [x] Live window previews on hover (DWM thumbnails, click to switch, ✕ to close) — verified
- [x] Jump lists (pinned / recent / frequent / tasks) in app menus — verified (Brave)
- [ ] End task for hung windows (IsHungAppWindow)
- [ ] App badges (unread counts) — needs notification access; investigate `UserNotificationListener` with a sparse package identity
- [ ] Drag files/folders onto the dock to pin; drag files over an app to bring it forward
- [ ] App groups / folder stacks; Trash on the dock
- [ ] Win+T keyboard navigation (RegisterHotKey + focusable app strip)
- [ ] Tray icons grid (needs Shell_NotifyIcon hosting or reading the tray toolbar — research)

## P1 — widgets
Done (45): clock, clock.digital, date, calendar, dayprogress, monthprogress, yearprogress, screentime, pomodoro, timer,
stopwatch, todos, notes, currency, sysstats, cpu, ram, network, battery, battery.dots, battery.ring,
quicktoggles, controlcenter, sysmon, brightness, themetoggle, notifications, showdesktop, taskmanager, apps, storage, recyclebin,
nowplaying, nowplaying.mini, nowplaying.art, volume, microphone, claude, codex, ailimits, stripe, crypto, weather, weather.sun, github.

Todo, roughly by value:
- [x] Wi-Fi card (scan, connect with key, disconnect) + Bluetooth card (paired + battery %, discovery, pair, remove) — verified
- [x] Control Centre (Windows quick settings: radios, airplane, dark mode, mic, battery saver, power mode, brightness, volume)
- [x] System Monitor (CPU / GPU / RAM / network + card with top processes)
- [ ] Calendar next event — Windows `Appointments` API needs package identity; Google Calendar via OAuth later
- [ ] Volume card: per-app mixer (IAudioSessionManager2) + output device switch (IPolicyConfig)
- [ ] Connect/disconnect paired Bluetooth audio (no public API; investigate KS reconnect like ToothTray)
- [ ] Do Not Disturb toggle (Focus Assist: `WNF`/quiet hours APIs — research), Keyboard (osk.exe), Language (keyboard layout switch)
- [ ] Top CPU / Top RAM tiles (per-process CPU% needs two samples; reuse SystemMonitor ticks)
- [ ] Temperature (WMI MSAcpi_ThermalZoneTemperature needs admin → research LibreHardwareMonitor-style alternatives)
- [x] Flip clock, analog minimal, world clocks, weather status, hourly, today & forecast, Top CPU / Top RAM
- [ ] Segment clock, clock & weather, 7 days, battery core / gauge, temperature, day timeline, stock watchlist
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

## Vela Island & macOS extras
- [~] Vela Island: timers, music, volume HUD, power; hover expand (rendered off-screen; live check pending)
- [x] App tiles, pinned/running divider, attention bounce, Downloads stack widget
- [ ] App folders (2×2 mini-icon tile), recent-apps section, minimize-to-dock hint

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
- [x] Settings migration framework (`Settings.Migrate`, `AppSettings.Version` = 2)
- [ ] App icon (.ico) + version resource; MSIX or Inno Setup installer; auto-start toggle verified
- [ ] Multi-monitor: choose display; per-monitor DPI changes while running
- [ ] Make the `chrome` render mode work (no region, exact-size window, DWM rounded corners) and compare CPU/memory
- [ ] Localisation-safe formatting (currently uses current culture for dates/numbers)
