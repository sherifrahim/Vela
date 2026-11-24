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
- [x] Tooltips open below a top dock (`ToolTip.Placement` / `ToolTip.Offset` resources, `ThemeManager.ApplyEdge`)
- [~] Auto-hide: off / smart (window overlaps) / always, edge trigger (code done; not hand-tested — owner was in a fullscreen app)
- [~] Adaptive theme: each display's dock matches its screen (colours + light/dark) — verified dark on display 2
- [~] Live drag-and-drop: dragged item follows the pointer, neighbours glide (code done, not hand-tested)
- [x] Magnified icons grow above the bar (`DockWindow.Headroom`, 18 DIP transparent band)
- [ ] Per-display app filtering ("show only windows on this display") for secondary docks
- [x] Lock dock setting (`LockDock`: no widget/app/edge drags unless the picker is open)

## P1 — apps (taskbar parity)
- [x] Start button (click = Start, right-click = quick menu shortcuts), pinned apps imported from taskbar
- [x] Running windows grouped by AUMID/exe, running dots, multi-window cycling, attention (flash) dot
- [x] Context menu: window list, new window, pin/unpin, close
- [x] Live window previews on hover (DWM thumbnails, click to switch, ✕ to close) — verified
- [x] Jump lists (pinned / recent / frequent / tasks) in app menus — verified (Brave)
- [x] End task for hung windows (`IsHungAppWindow`; menu item only when not responding)
- [x] App badges (unread counts) — `UserNotificationListener` works unpackaged (access "Allowed"); `Services/NotificationService.cs`
- [ ] Drag files/folders onto the dock to pin; drag files over an app to bring it forward
- [ ] App groups / folder stacks; Trash on the dock
- [ ] Win+T keyboard navigation (RegisterHotKey + focusable app strip)
- [x] Tray icons grid (`Shell/TrayHost.cs`: second Shell_TrayWnd that forwards everything to Explorer; Start verified)

## P1 — widgets
Done (45): clock, clock.digital, date, calendar, dayprogress, monthprogress, yearprogress, screentime, pomodoro, timer,
stopwatch, todos, notes, currency, sysstats, cpu, ram, network, battery, battery.dots, battery.ring,
quicktoggles, controlcenter, sysmon, brightness, themetoggle, notifications, showdesktop, taskmanager, apps, storage, recyclebin,
nowplaying, nowplaying.mini, nowplaying.art, volume, microphone, claude, codex, ailimits, stripe, crypto, weather, weather.sun, github.

Todo, roughly by value:
- [x] Wi-Fi card (scan, connect with key, disconnect) + Bluetooth card (paired + battery %, discovery, pair, remove) — verified
- [x] Control Centre (Windows quick settings: radios, airplane, dark mode, mic, battery saver, power mode, brightness, volume)
- [x] System Monitor (CPU / GPU / RAM / network + card with top processes)
- [x] Calendar next event — private iCal links (Google / Outlook / iCloud), `Connectors/CalendarConnector.cs` + `Ics` parser
- [x] Volume card: per-app mixer (IAudioSessionManager2) + output device switch (IPolicyConfig) — `Services/AudioMixer.cs`, read on demand
- [ ] Connect/disconnect paired Bluetooth audio (no public API; investigate KS reconnect like ToothTray)
- [ ] Do Not Disturb toggle (Focus Assist: `WNF`/quiet hours APIs — research), Keyboard (osk.exe), Language (keyboard layout switch)
- [x] Top CPU / Top RAM tiles (shared sampler)
- [x] Temperature: GPU sensor via WDDM adapter data, NVML fallback (2026-11-24). CPU temperature still needs a kernel driver
- [x] Flip clock, analog minimal, world clocks, weather status, hourly, today & forecast, Top CPU / Top RAM
- [~] Segment clock, clock & weather, 7 days, battery core / gauge, stock watchlist done; day timeline open
- [ ] Now Playing compact + bar variants (per-app mixer done: Speakers tile → "Output and apps")
- [ ] Quick toggles: hotspot, airplane, energy saver, Ethernet status, user-selectable toggles
- [x] Weather hourly / today & forecast / status / sunrise tiles
- [ ] Brightness for external monitors (DDC/CI via dxva2 `SetMonitorBrightness`)
- [ ] Search / Commands launcher, Recently opened, Screenshot capture
- [~] AI: DeepSeek, GLM, OpenCode done; Gemini (Antigravity), Grok, Cursor open
- [x] Finance: Polar, Dodo, Shopify, Lemon Squeezy, Paddle, Gumroad, Total Revenue (+ charts) — untested with real keys
- [x] GitHub activity grid / overview, Gmail (own OAuth client, loopback redirect), LeetCode
- [~] Analytics & developer: Plausible, Dub, Resend, Linear, Supabase done; DataFast, Search Console open
- [~] Stocks: watchlist done (Finnhub); chart open (candles are paid on Finnhub)
- [ ] Social: YouTube (API key), X, TikTok, Instagram
- [ ] Wallpaper gallery

## Vela Island & macOS extras
- [~] Vela Island: timers, music, volume HUD, power; hover expand (rendered off-screen; live check pending)
- [x] App tiles, pinned/running divider, attention bounce, Downloads stack widget
- [ ] App folders (2×2 mini-icon tile), recent-apps section, minimize-to-dock hint

## P2 — glass & animation
- [x] Game Mode (2026-11-24): glass captures off + low priority while a game is in front (`Services/GameMode.cs`)
- [x] Window previews: Aero Peek on hover, middle-click close (2026-11-24)
- [ ] Composition frost for `BackdropWindow` (HostBackdropBrush + blur + saturation, rounded AA clip; see PROGRESS)
- [ ] Live drag reorder with a sliding neighbour gap (OpenTaskbar #130)
- [x] Live Liquid Glass (2026-11-19): ps_3_0 refraction shader over the wallpaper + Windows.Graphics.Capture of the windows
      behind the dock (`Theming/LiquidGlass.cs`, `Theming/WindowCapture.cs`). Still to do: same lens on cards/picker.
- [ ] App launch bounce ✔ exists; add minimize-to-dock "genie"-style hint animation on the icon
- [ ] Mac-style window open/close/minimize animations for *other* apps: only possible by injecting into
      DWM/explorer (that's what Windhawk mods do). Out of scope for Vela core; if wanted, ship as a separate,
      opt-in Windhawk mod in `extras/` and document the risk.

## P2 — notifications & automations
- [x] Notification listener (badges, WhatsApp tile, Notifications count) — no package identity needed
- [ ] Automations: AI limit warning toast, timer/pomodoro end toast (currently a system sound), quiet hours
- [~] Global hotkeys: Alt+T = PowerShell as admin (owner chose this over the reference's Alt+T todos; `AdminShellHotkey`, main dock owns it). Win+T apps still open (Explorer owns Win+T)

## Engineering
- [x] Unit tests (xUnit, `tests/Vela.Tests`, 14 tests) + `tools/smoke.ps1` — by Codex, session 5
- [x] Settings migration framework (`Settings.Migrate`, `AppSettings.Version` = 2)
- [ ] App icon (.ico) + version resource; MSIX or Inno Setup installer; auto-start toggle verified
- [ ] Multi-monitor: choose display; per-monitor DPI changes while running
- [ ] Make the `chrome` render mode work (no region, exact-size window, DWM rounded corners) and compare CPU/memory
- [ ] Localisation-safe formatting (currently uses current culture for dates/numbers)
