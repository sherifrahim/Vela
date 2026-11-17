# Progress log

Newest entry first. Every agent session appends one entry (see AGENTS.md → Handoff protocol).

---

## 2026-11-17 — Claude (Opus 5.5) — session 6: closer to the reference (colours, cards, Start, defaults)

Owner: "are we like lumabar now? … change the behaviour and themes and colours to match; you can overwrite my settings."
Compared renders against `reference/lumabar/assets/*` with sampled pixel colours.

**Changed (uncommitted)**
- Cards are solid like the reference: `Flyout.Background` = #181818 on Black / Liquid Glass (no live blur under cards);
  the picker and control panel stay frosted via the new `Panel.Background` key. `Card.Header`: 15.5 semibold title,
  plain ✕, hairline rule edge to edge under the header.
- White style is a solid white bar (#FFF, tiles #F3F3F3); bar shading only on glass styles. `GlassOn` no longer forced
  on by `AdaptiveTheme` (glass follows the chosen style; "Match the screen" only flips light/dark).
- Start = light Windows mark on a dark rounded tile (`WindowsLogo.StartTile`); the focused app gets a thin accent
  outline around its icon (`AppItem._ring`, scales with magnification).
- Defaults: `Magnification` off; default layout = battery, nowplaying.mini, volume, quicktoggles, battery.dots, apps,
  screentime, claude, clock (the reference order). The owner's settings were reset to these
  (backup: `%LOCALAPPDATA%\Vela\settings.before-reference-reset.json`; pinned apps and weather kept).
- Tabular figures only for number-like text (`UI.NumberLike`): Inter's tabular set widened hyphens ("sign - in").
- `--render` also writes `cards.png` (Claude, ChatGPT, calendar cards).

**Verified:** build 0/0, tests 14/14, renders (glass/white/midnight + cards), live dock on both displays, no new log
warnings.

**Later the same session (owner: "keep working"):**
- Lock dock (`LockDock`): widgets, apps and the bar edge only move while the picker is open.
- End task for hung apps: app menu shows "End task (not responding)" when `IsHungAppWindow` (kills the owning process).
- Tooltips open below a top dock (`ToolTip.Placement`/`ToolTip.Offset` resources set by `ThemeManager.ApplyEdge`).
- Alt+T = PowerShell **as administrator** (owner asked for this instead of todos): `AdminShellHotkey`, RegisterHotKey on
  the main dock (`DockWindow.SyncHotkeys`), `WinActions.LaunchShell` (pwsh 7 if installed, runas off the UI thread;
  UAC cancel ignored). Registration verified (another process can't claim Alt+T); the key itself not pressed (owner gaming).
- New `terminal` widget (System): card with PowerShell / Command Prompt, normal or admin; middle-click = PowerShell.
  Added to the owner's dock after the apps.
- Sound card (click the device name on the Speakers tile, or right-click → "Output and apps…"): output devices (click =
  default for every role via the undocumented-but-stable `IPolicyConfig`), master slider, one slider per app from
  Core Audio sessions (grouped by exe; click the icon to mute). `Services/AudioMixer.cs` reads on demand only; session COM
  refs are released when the card unloads. Rendered with real data (2 devices, 2 apps); device switching NOT exercised
  live (would have moved the owner's game audio).
- 11 local widgets (`Widgets/LocalWidgets.cs`): Date, Today, 7 Days, Segment Clock, Clock & Weather, Battery Gauge,
  Battery Core, Keyboard, Language, Weather Details, Claude Usage (tokens). Open-indicator setting (dot / large / line).
- Account widgets (owner: "let users connect and enjoy them"): `MetricConnector` / `RevenueConnector` bases + 16
  connectors in `Connectors/AccountConnectors.cs`, one generic `MetricWidget` (+ chart variant) and `TotalRevenueWidget`
  in `Widgets/AccountWidgets.cs`; they appear on Settings → Connections automatically. Two-part inputs (site + key,
  store + token, key + symbols) go in one field separated by a space. Endpoints checked (401/403 without a key),
  LeetCode checked end to end; **no connector has been tried with a real key**.
- `docs/PARITY.md`: 81 of the reference's 109 widgets; what is missing and why.
- Then 10 more (`Widgets/ExtraWidgets.cs` + GitHub Overview connector + Stripe Chart): World Time, Day Timeline,
  Recently Opened (skips ms-* protocol entries), Search (Win+S), Commands (run box + quick actions), Screenshot
  (ms-screenclip:), Now Playing compact / bar, Stripe Chart, GitHub Overview. Now 91 of 109.
- `--render … --public` leaves out widgets with personal data (location, process names, recent files) and the cards;
  `docs/screenshots` (dock, dock-white, dock-midnight, widgets) were regenerated with it. README updated (98 widgets).
- Gmail + Search Console (own Google OAuth client, PKCE, loopback listener; `GoogleAuth`), GLM Coding Plan, OpenCode
  (local history). `MetricConnector` gained NeedsSignIn / SignInAsync / Disconnect and local (no-key) connectors.
  Tests 17/17 (OpenCode parser, Google loopback). Now 95 of 109; see docs/PARITY.md for what each still needs.
- Notifications (owner granted permission): `Services/NotificationService.cs` uses `UserNotificationListener`, which
  works **unpackaged** (access was already "Allowed"); polls every 4 s only while subscribed. App badges on dock icons
  (match by AUMID, by packaged-app install folder, or by name; `AppBadges` setting), Notifications tile with count +
  list (dismiss / clear all), WhatsApp tile (unread chats + latest message). Verified live: Claude 1, ChatGPT 6.
- Calendar events via private iCal links (Google / Outlook / iCloud), `Ics` parser (RRULE, EXDATE, RECURRENCE-ID,
  TZID, all-day); the Calendar tile shows the next event. Tests 24/24. No real iCal link tried yet. Now 96 of 109.
- Notification polling: change events aren't delivered to unpackaged apps, so it polls every 10 s and only parses new
  notifications. Badges off vs on measured 1.33 % vs 1.02 % of a core (noise) — but the whole dock now idles at ~1 %
  while the owner is active (game + apps), vs 0.26 % measured earlier when idle. **Next up: profile idle CPU on a quiet
  PC** (WinEvent hook traffic? adaptive sampler? Claude log scan?) and get it back under 0.5 %.
- Found it: the EVENT_OBJECT_LOCATIONCHANGE hook (ScreenEvents) also fires for the mouse cursor on every movement.
  Replaced with a 1 Hz foreground-window rect check on the shared ticker (MOVESIZE/MINIMIZE hooks stay). Measured
  while the owner was active: 1.0–1.3 % → 0.55 % of a core, 68 → 57 MB. Next: profile the remaining 0.5 % on a quiet PC.
- Correction: that 0.55 % was measured on the old, still-exiting process. Per-thread sampling of the new build:
  ~60 ms / 30 s UI thread + ~80 ms render thread (the layered dock redrawn each second) ≈ 0.5 %. Next: skip
  redraws when no visible text changed.
- "Doesn't start with Windows": the Run key is correct; the PC only slept (no sign-in), and Vela had been quit
  before. `tools/dev.ps1` now waits for the old dock to exit (it used a fixed 3 s and could leave no dock) and starts
  the new one with --wait-for-exit. Notification log spam fixed (event subscription tried once). Not yet verified
  through a real sign-in.

**Next up**
1. Picker: structure already matches `widgets-preview.webp`; only polish left (outlined close button, colour category
   icons). Not renderable off-screen yet (needs a DockWindow) — add a `--render` path if you touch it.
2. Claude tile shows "sample" plan rows here: the desktop app's Code tab writes no `~/.claude/.credentials.json`.
3. Quick Toggles: Bluetooth pill neutral when on but no device connected? (reference shows it grey) — confirm.

---

## 2026-11-16 — Claude (Opus 5.5) + Codex — session 5: live blur everywhere, first joint pass with Codex

**Released** v0.4.0 (premium pass) at the start of this session.

**How we worked:** the owner asked Claude and Codex to collaborate. Claude briefed Codex (the app's bundled CLI,
`codex exec`, session `01a1e539-e5d4-70d1-80c0-60208f18c780`), Codex reviewed the repo read-only, then both
implemented in parallel with a strict file split (Codex: AudioService, Services, ReferenceWidgets, AppsWidget,
WindowPreview, LiveReorder, tests/, tools/smoke.ps1; Claude: everything else + docs). Codex built only to
`.codex-build/`, never touched the running dock, never committed.

**Changed (uncommitted)**
- Live blur, done properly: `Views/BackdropWindow.cs` (accent Acrylic, non-layered, rounded region) under every dock
  bar and — via `GlassWindow.LiveBackdrop` — under cards, the widget picker and the control panel, which are now
  translucent frosted glass (`Flyout.Background` alpha 0xC4 dark / 0xD6 light while `LiveBlur` is on).
  `Vela.exe --blur-test` compares materials on the second display.
- Claude: crash-proof staged `App.Cleanup` (taskbar restore always runs last); per-display fullscreen detection;
  island morph (one animated `Expansion` drives height/width/radius/padding); 18 DIP headroom above the bar for
  magnified icons (`DockWindow.Headroom`, bar no longer clipped); adaptive-sampler hash covers all channels; app
  settings saved with the "pins" key; EditMode handler leak fixed; ScreenEvents ignores Vela's own windows (a
  self-triggered loop had cost 6 % CPU).
- Codex: native Core Audio callbacks (no 1 Hz polling), one race-guarded shared top-process sampler, cached async
  Downloads snapshot, frame-rate-independent magnification, preview rows that fit the display, LiveReorder cleanup,
  `tests/Vela.Tests` (14 xUnit tests), `tools/smoke.ps1`.
- Bugs found while testing: card backdrop created inside a render pass (zero HWND) → deferred; backdrop window
  stole activation and closed cards → `EnsureHandle` + card re-activates itself if a backdrop grabs focus.

**Verified**
- `dotnet test`: 14/14 pass. Build 0 warnings / 0 errors. `--quit` logs "Vela stopped cleanly".
- Live blur under the dock and under the Control Centre card, over a striped test window on display 2.
- Idle (2 docks, settled): 109 ms CPU / 30 s (~0.36 % of a core), WS ~59 MB.

**Not verified** (owner was playing Days Gone fullscreen on display 1 — one synthetic click landed in the game;
no further input was sent there): main-display magnification headroom, island morph live, auto-hide, previews.

**Follow-up (same day, after owner feedback "looks like a 2005 Nokia theme", "black box overlay over the icons"):**
`AppTiles` now defaults to false (loose icons on the glass, like the reference); hover/active never draws a box behind
loose icons (the running dot is the only state mark). Liquid Glass toned down: bar rim 0x9C→0x3A, sheen 0x2E→0x10, tile
rim 0x66→0x2E, bar border top/bottom 0x40/0x14 → 0x2E/0x1A (closer to a uniform macOS hairline). Rendered off-screen
only; uncommitted.

**Next up**
1. Live-check on display 1: magnification rising above the bar, island morph, frosted picker/control panel.
2. Round 2 with Codex: its remaining items (smoke script run on a free machine, audio device-switch card) and the
   unified per-dock visibility state machine (Claude, `DockWindow`).
3. `BackdropWindow` for `WindowPreview` (non-layered already: apply the accent to the preview window itself).

---

## 2026-11-16 — Claude (Opus 5.5) — session 4: release 0.3.0, lumabar deep-dive, premium pass, Vela Island

**Released** v0.3.0 (github.com/sherifrahim/Vela/releases/tag/v0.3.0) before this work.

**Research (lumabar.xyz, read in the owner's Chrome)** — findings now driving the design:
- Their UI font "lumabar UI" is **SF Pro Display** (not licensable for Windows) → Vela bundles **Inter Display**
  (OFL, `Assets/Fonts`, `Font.UI` / `UI.Font`), the closest open match.
- Hero dock CSS (tile 64 px): bar radius 21.6 = tile radius 17.6 + padding 4; tiles `rgba(0,0,0,.18)` +
  1 px `rgba(255,255,255,.12)`; glass = canvas blur + `rgba(20,20,20,.4)` + inset shadows
  (0 -10 24 rgba(0,0,0,.28), 0 10 24 rgba(255,255,255,.04)); app icons 37/64 of the tile, **each app in its own tile**.
- Type at tile 64: labels 10.5/600 (+0.16 px tracking), secondary 9.5/400 at 55 % white, values 11/700 tabular,
  clock 17/600, weekday 10/700 uppercase red +0.08 em, big numbers 19/500. Vela's scale follows ×0.656.
- 86 widget renders were studied (kept in the agent scratchpad only, never committed).

**Changed (committed as 0.4.0)**
- Inter Display bundled + type scale reworked (semibold values, medium secondary, tabular numerals).
- Liquid Glass palette retuned to the measured values; new `Bar.Shade` inner-shading layer.
- App tiles (`AppTiles`, default on), pinned/running divider, attention bounce.
- **Vela Island** (`Views/IslandWindow.cs`, `Island` hub, setting `Island`): black pill at the top of the main
  display for timers (Pomodoro/Timer/Stopwatch publish via `TimerTileBase.PublishIsland`), music, volume HUD,
  power plug/unplug; hover expands (190 ms); hides in fullscreen.
- New widgets (`Widgets/ReferenceWidgets.cs`): weather.status, weather.hourly, weather.forecast, clock.flip,
  clock.analog, clock.world, topcpu, topram, downloads (stack). Unconnected Claude/Stripe/GitHub tiles show a
  labelled sample instead of dashes.
- `Vela.exe --render <dir>` (`Views/RenderCatalog.cs`): dock + catalogue + island to PNGs off-screen.
- Fix: the shared ticker paused when *any* display went fullscreen (froze the other display's clock); now only
  when all docks are hidden.
- Third-party notices: Inter OFL added; `publish.ps1` copies `Inter-OFL.txt`.

**Verified**
- Builds 0 errors; no ERROR lines. `--render` output reviewed: dock, all 53 catalogue widgets, island
  (timer compact/expanded, volume, power) look right.
- Idle (2 docks, adaptive heartbeat, island): 94 ms CPU / 20 s (~0.47 % of a core), WS 30 MB.

**Follow-up in the same session (uncommitted):** real **live blur** — `Views/BackdropWindow.cs` (accent Acrylic
under each bar, `LiveBlur` setting, `--blur-test`), verified on display 2 over a striped test window: colours behind
the dock blur through it live. Fixed a self-triggered event loop (6 % CPU → 0.3 %).

**Not verified** (owner had an Unreal Engine app fullscreen on display 1 the whole session — the main dock and
the island were hidden; no synthetic input was sent)
- Island live behaviour (music, hover expand, click), app tiles + magnification feel on the main display,
  Downloads card, Top CPU/RAM values, adaptive light mode.

**Next up**
1. Hand-check the island and the main dock when display 1 is free; tune `IslandWindow` sizes.
2. Idle CPU crept to ~0.47 %: profile `AdaptiveBackdrop` heartbeat (2.5 s) and `TopProcessesWidget` (10 s,
   `Process.GetProcesses`); consider a 5 s heartbeat and skipping when the strip hash is unchanged.
3. Port more reference renders: battery core / gauge, temperature, day timeline, stock watchlist, now-playing bar,
   GitHub activity grid, total revenue; WPF has no letter-spacing, so emulate tracking only where it matters
   (weekday labels) with a custom glyph-run element if needed.
4. App folders (several apps in one tile with a 2×2 mini-icon preview, like the reference "group" tile).

---

## 2026-11-16 — Claude (Opus 5.5) — session 3: OpenTaskbar ideas, live drag, previews, jump lists, adaptive theme, auto-hide

**Owner asks:** take the good parts of github.com/alztrk/opentaskbar (MIT); live drag-and-drop rearranging;
the dock's look must follow what's on screen, per display.

**Changed (uncommitted — owner hasn't asked for a commit this session)**
- `UI/LiveReorder.cs`: live drag for widgets (`DockWindow`) and app icons (`AppsWidget`): item follows the
  pointer, neighbours glide (FLIP, 170 ms).
- `Views/WindowPreview.cs`: live DWM window previews on app hover (non-layered host window, click to switch,
  ✕ to close); app tooltips suppressed for running apps; setting `WindowPreviews`.
- `Shell/JumpLists.cs`: OpenTaskbar's native jump list reader, ported (MIT; `THIRD_PARTY_NOTICES.md`, copied into
  `publish/` by `tools/publish.ps1`); app menus fill in Pinned/Recent/Frequent/Tasks asynchronously.
- `Theming/AdaptiveBackdrop.cs` + `ThemeManager.Variant` + `Shell/ScreenEvents`: per-display adaptive look
  (`AdaptiveTheme`, default on): glass = blurred strip of that display beside the bar; light/dark per dock.
- `Views/DockAutoHide.cs`: auto-hide off/smart/always (`AutoHide`), edge trigger; releases the reservation.
- Icons now extracted at 96 px (`IconCache.Get` default). Control panel: Match the screen, Auto-hide,
  Live window previews.

**Verified**
- Builds 0 errors; no ERROR lines in vela.log.
- Window preview for Claude: card with icon, title, ✕ and live thumbnail above the icon (magnified).
- Jump list in Brave's menu (recent files with icons) above the window list.
- Adaptive: display-2 dock sampled its screen and switched to the dark set (log "adapts to a dark screen").
- Fullscreen: an Unreal Engine fullscreen app on display 1 hid only that dock (QUNS=3); display 2 unaffected.
- Idle with two docks + adaptive heartbeat: 62 ms CPU / 20 s (~0.3 % of a core), WS 56 MB, private 140 MB.

**Not verified** (owner was in a fullscreen app; no synthetic input sent after noticing)
- Live drag feel (widgets + icons), auto-hide all modes, adaptive light switch (needs a bright app above the
  dock), jump-list task invocation, preview ✕ close, 96 px icon sharpness.

**Next up**
1. Hand-test the list above; tune `LiveReorder` timings and `AdaptiveBackdrop` thresholds (0.42 / 0.62).
2. `AdaptiveBackdrop`: consider sampling *behind* the bar when ReserveSpace is off or auto-hide is on
   (needs `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)` on the dock — but that hides the dock from the
   owner's screenshots; make it opt-in).
3. Jump-list icons for tasks (OpenTaskbar extracts resource icons via `PrivateExtractIcons`).
4. Volume mixer card; per-display app filtering; magnified icons above the bar (see session 2 list).

---

## 2026-11-15 — Claude (Opus 5.5) — session 2: reference proportions, full-width bar, cards, control centre, multi-display

**Owner feedback this session:** dock looked "chunky / cheap knockoff", didn't span the screen, sat on top of
apps; cards opened mid-screen; Wi-Fi/BT opened Windows Settings; wanted a control panel, quick settings,
peek/show desktop, Mac-style magnification, rearrange anything, glass options, stacks, system metrics,
Bluetooth battery, clock → Clock app / calendar → calendar, a dock on every display.

**Git:** history rewritten on request — `main` is now a single root commit `6abe471` (same tree as the old
`addfea4`). The remote (`sherifrahim/Vela`) still has the old 4-commit history; publishing needs a force-push,
(force-pushed later on request). Session 2 work committed + pushed. Afterwards: fixed control-panel pages being unreachable (sidebar DragMove swallowed clicks) — verified Appearance → Style switching live.

**Changed**
- Measured the reference (`reference/lumabar/assets/desktop-preview.webp`: 1 px there ≈ 1 DIP on the owner's
  2560×1600 @175 % screen) and re-tuned everything: tile 42 DIP / radius 12, bar padding 3 / radius 16,
  `Hairline` (1 physical px) borders, type scale (captions 8.5, values 16, clock 12.5, notes 7). All widgets re-sized.
- Full-width bar (`BarWidth` full|fit), `Views/DockRow.cs` (apps centred on screen, widgets hug the ends),
  dynamic fit (row scales ≥ 60 % when too wide), soft shadow, anti-aliased corners (no Win32 region any more).
- `WindowTracker.FitMaximizedToWorkArea`: windows maximized before Vela hid the taskbar are re-fitted.
- `FlyoutWindow`: positioned via Left/Top DIPs over the anchor (fixes mid-screen cards); `Navigate()`; opaque
  panels; memory trim 4 s after cards close.
- New: `Services/Connectivity.cs` (WifiService, BluetoothService incl. battery %, QuickSettingsService power mode),
  `Views/QuickCards.cs` (Wi-Fi card, Bluetooth card, Control Centre), `GpuMonitor` (PDH),
  `Widgets/StackWidget.cs`, widgets `controlcenter`, `sysmon`, `calendar`, `nowplaying.art`.
- Desktop corner (peek + show desktop), drag-to-reorder any time, drag app icons to reorder/pin,
  macOS magnification (`AppsWidget` MagFrame), blue Windows 11 Start mark, watch icon for Screen Time.
- Glass options: `GlassEffect` auto|off|frosted|liquid, `GlassBlur`, `GlassTiles` (Theme.cs + WallpaperGlass vibrancy).
- Control panel rewrite (`Views/SettingsWindow.cs`, sidebar pages incl. Widgets/Apps management).
- Multi-display: one `DockWindow` per display (`DockWindow.All`, `For()`, `SyncDisplays`, `SaveWidgets`).
- Settings migration framework (`Settings.Migrate`, v2 adds `controlcenter` before the clock).
- Clock: date half → calendar card (+ "Open Calendar"), time half → Windows Clock app (`ms-clock:`).
- All source files normalised to LF (git already stored LF; no content diff).

**Verified on the owner's PC** (screenshots + synthetic clicks)
- Builds 0 warnings / 0 errors; no ERROR lines in vela.log.
- Dock: full width with rounded ends, apps centred, new proportions; second dock on DISPLAY2 (1920×1080 @100 %,
  auto-scaled to 86 %).
- Bluetooth card opens above the BT pill, lists the paired WH-CH720N; Wi-Fi card lists real networks with
  the connected one first; Control Centre opens above its tile (power mode, brightness, volume live).
- Control panel renders (General page). Magnification falloff visible on hover.
- Idle with two docks: 31 ms CPU / 10 s, working set 38 MB, private 151 MB.

**Not verified yet**
- Glass Frosted/Liquid/blur slider/glass tiles visually; widget stacks; drag-reorder of widgets and pins;
  desktop peek (undocumented DWM call); Bluetooth pairing of a new device; Wi-Fi connect with a key;
  battery % (depends on the headset reporting it); top edge; fullscreen hiding per display; the other
  control-panel pages; `sysmon`, `calendar`, `nowplaying.art` tiles in the dock.

**Next up (in order)**
1. Hand-test the "Not verified" list; fix what breaks (start with stacks + glass options).
2. `Views/QuickCards.cs`: volume/mixer card (IAudioSessionManager2 per-app volume, IPolicyConfig output switch).
3. Let magnified icons rise above the bar: taller transparent window band in `DockWindow.Reposition`, move the
   clip from `Bar` to the tile layer, keep the appbar reservation unchanged.
4. Secondary docks: optional "only this display's windows" filter in `AppsWidget.BuildGroups` (MonitorFromWindow).
5. More reference widgets (picker categories Analytics, Social, Stocks): weather variants, world clocks,
   flip clock, Polar/Lemon Squeezy/Gumroad revenue, YouTube, Plausible.
6. Tooltips below the bar when docked at top; auto-hide dock mode.

---

## 2026-11-15 — Claude (Opus 5.5) — session 1: from zero to a working dock

**Context discovered**
- The cloned repo (`Acadbek/lumabar`) contained only lumabar's README + screenshots: lumabar is closed-source
  Electron. Moved those to `reference/lumabar/` (reference only). `git remote origin` still points at that
  repo — the owner should point it at their own repo before pushing anything.
- Installed .NET SDK 10.0.404 (winget). Desktop runtime 10.0.11 was already present.

**Built**
- Full WPF app `src/Vela` (≈45 files): dock window + appbar + taskbar hide/restore, window tracker + apps
  strip with real icons, 41 widgets, 5 styles, widget picker, settings, cards, 4 connectors
  (Claude, Codex, GitHub, Stripe), screen-time tracker, wallpaper glass, dev tools in `tools/`, docs.

**Verified on the owner's PC (2560×1600 @175 %)**
- Builds with 0 errors. Dock renders at the bottom with the default layout: Battery, Now Playing Mini,
  Speakers slider (reads real volume 32 %), Wi-Fi/BT pills, Start + real pinned apps with running dots,
  Screen Time, Claude (233.6M tokens/week from local logs, top model Opus 4.8), clock.
- Widget picker and Settings open (`--widgets`, `--settings`) and render correctly.
- `--quit` → "Vela stopped cleanly", taskbar marker removed (taskbar restored).
- Idle: 0.16 % of one core; working set 37 MB after trim; private 114 MB.

**Learned / decided**
- Non-layered WindowChrome glass + window region = black window → default is layered windows.
- DWM blur on layered windows ignores the region → disabled; dock glass = blurred wallpaper slice.
- Claude limits need `~/.claude/.credentials.json`; this PC has none (Claude desktop app holds the
  sign-in), so the Claude tile shows tokens + "sign in". Expected behaviour, not a bug.

**Not verified yet (no mouse access this session)**
- Clicking: cards for each widget, context menus, drag-to-top, drag-reorder, pomodoro/timer flows,
  todos/notes editing, connector key entry, fullscreen hiding, Explorer restart recovery.

**Next up (in order)**
1. Hand-test the "Not verified" list above; fix what breaks. Start with cards (`FlyoutWindow.Position`)
   and the context menus on the non-activating dock.
2. Tooltips below the bar when docked at top (`Theming/Controls.xaml` ToolTip style + DockWindow).
3. Auto-hide dock mode (ROADMAP P0).
4. Live window previews (DwmRegisterThumbnail) and jump lists (ROADMAP P1 apps).
5. Calendar widget, Do Not Disturb, Top CPU/RAM.
6. xUnit test project for the parsers (Claude/Codex logs, Stripe sums, WallpaperGlass mapping).
