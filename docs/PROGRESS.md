# Progress log

Newest entry first. Every agent session appends one entry (see AGENTS.md → Handoff protocol).

---

## 2026-11-24 — Claude (Opus 5.5) — session 9: composition frost behind every glass panel

Owner: "Yes, build it" (next-up 1 of session 8).

**Changed (committed)**
- `Theming/CompositionFrost.cs` (new): Windows.UI.Composition frost on the backdrop window. `DesktopWindowTarget`
  (via `ICompositorDesktopInterop`, DispatcherQueue created on the UI thread), one sprite: `HostBackdropBrush` →
  Gaussian blur σ16 (hard border) → saturation 1.65, clipped by an anti-aliased rounded geometry. Recipe from WacOS
  `2beced5` (MIT); code is ours. The effect graph is two hand-made COM objects (`D2DEffect`, ComWrappers) instead of
  Win2D. Any failure → logged once, accent Acrylic as before.
- `Views/BackdropWindow.cs`: new `Kind.Frost` (now `Best`); region radius smaller than the clip (keeps the soft edge,
  only far-corner clicks pass); swallows `WM_DPICHANGED` (WPF shrank the window 980 → 560 px when it moved from the
  175 % display to the 100 % one; the same resize would have clipped the accent blur there too).
- `--blur-test`: top row is now the frost, then the accent variants.
- `tools/screenshot.ps1`: per-monitor DPI aware; `-Secondary` captures the other display.

**Verified:** build 0/0, tests 28/28, picker on the left display fully frosted with smooth corners (6× zoom),
`--blur-test` (frost noticeably more saturated than accent Acrylic, rounded corners vs square), both bars on the frost
in Game Mode (temporary games.txt entry, removed), catalogue render OK, no ERROR lines, `--quit` → "Vela stopped
cleanly". CPU unchanged at idle (the bars use the lens in Liquid Glass, so no frost is created until a panel opens or
the lens is off; the frost itself is drawn by DWM).
**Not verified:** cards/control panel on the primary (175 %) display by eye; Windows 10 (should fall back cleanly);
"Transparency effects" off in Windows settings.

**Next up**
1. Owner looks at the frost over colourful content; tune `CompositionFrost.BlurSigma` / `Saturation` if needed.
2. OpenTaskbar's live drag reorder with a sliding neighbour gap (`Views/Windows/MainWindow.DragDrop.cs`, #130) for
   `Views/DockRow.cs`.
3. Per-app media volume (OpenTaskbar `MediaWidget`: AUMID → WASAPI session) for Now Playing.
4. `tools/smoke.ps1` fails under Windows PowerShell 5.1 (`$PSScriptRoot` empty in param defaults): resolve the
   defaults inside the script body.

---

## 2026-11-24 — Claude (Opus 5.5) — session 8: salvage from OpenTaskbar and WacOS

Owner: salvage what's possible from OpenTaskbar (updated) and WacOS. Licences checked first: OpenTaskbar is MIT.
WacOS today is "source-available, no reuse in any project" — but commit `2beced5` (2026-11-20) was published under
MIT, so only that snapshot is used (see AGENTS.md → Third-party code, THIRD_PARTY_NOTICES.md).

**Changed (committed)**
- Liquid Glass lens recalibrated to WacOS's macOS 26 measurements (`Theming/LiquidGlass.cs`, `DockWindow.ShowLens`):
  Snell's-law squircle bezel (n = 1.5; band 25 % of height ≤ 20 px, pull 80 % ≤ 60 px), ±10 % chromatic split,
  saturation 1.8, 5 % veil, 2.5 % grain, thin top-lit specular wire. Frost = BlurEffect σ 0.6–11.6 DIP (Blur strength).
- **Temperature** widget (`temperature`): GPU sensor via WDDM `D3DKMTQueryAdapterInfo` (NVML fallback), no driver
  (`Services/GpuThermal.cs`, `GpuMonitor.TempC`). Reads 55 °C on the owner's PC (test `HardwareTests`).
- **Cursor Usage** widget (`cursor`): browser PKCE sign-in, tokens in Secrets, `GetCurrentPeriodUsage`/`GetPlanInfo`
  (`Connectors/CursorConnector.cs`). Settings shows "Sign out" for browser sign-ins (`MetricConnector.CanSignOut`).
  Endpoints answer (404 poll / 401 usage without a session); **never run with a real Cursor account**.
- **Game Mode** (`Services/GameMode.cs`, Settings toggle, default on): engine window classes, store folders, exclusive
  D3D fullscreen, or `%LOCALAPPDATA%\Vela\games.txt`. While on: lens/captures off (live blur), below-normal priority +
  EcoQoS; off ~4 s after the game's windows are gone. Verified with a test entry (log + BelowNormal priority).
- Window previews: Aero Peek after 500 ms on a card (honours EnableAeroPeek), middle-click closes the window; Vela's
  glass windows, backdrops and the preview are excluded from peek; cleanup ends any peek.

**Looked at, not taken:** OpenTaskbar's wireless peripheral batteries (no Logitech/Razer/SteelSeries device on this PC),
Antigravity/Gemini usage (needs the Gemini CLI sign-in; not installed), WacOS screen-capture glass (it hides the shell
from every screenshot/screen share via WDA_EXCLUDEFROMCAPTURE; Vela's per-window capture doesn't).

**Verified:** build 0/0, tests 28/28, dock restarted on both displays with no ERROR lines, catalogue render shows both
new widgets, `--quit` → "Vela stopped cleanly".

**Next up**
1. Composition glass for `Views/BackdropWindow.cs`: WacOS (`2beced5`, `src/shell/backdrop.cpp` MakeFrost/Apply) draws
   frost as Windows.UI.Composition `HostBackdropBrush` → GaussianBlur σ16 → Saturation 1.65, clipped by an
   anti-aliased rounded geometry (any radius). Needs a DispatcherQueue + `ICompositorDesktopInterop` in C#.
2. OpenTaskbar's live drag reorder with a sliding neighbour gap (`Views/Windows/MainWindow.DragDrop.cs`, #130) for
   `Views/DockRow.cs`.
3. Per-app media volume (OpenTaskbar `MediaWidget`: AUMID → WASAPI session) for Now Playing.
4. Tune the lens by eye with the owner over colourful content.

---

## 2026-11-19 — Claude (Opus 5.5) — session 7: real Liquid Glass (refraction, live)

Owner: "i need that og liquid glass or blur, achieve it at any cost."

**Built (committed in 3696164)**
- `Theming/LiquidGlass.cs`: HLSL pixel shader compiled at startup with Windows' own `d3dcompiler_47.dll` (ps_3_0,
  ~2.3 KB) + `LiquidGlassEffect` (WPF `ShaderEffect`). Rounded-box SDF → surface normal → lens profile: the flat
  middle stays clear, the rim pulls the image inwards (refraction), splits red/blue (dispersion), and catches light
  on the edge facing the light plus a dimmer bounce opposite.
- `Theming/WindowCapture.cs`: Windows.Graphics.Capture of **one window** (not the screen, so Vela is never in its
  own input and the dock still appears in screenshots). Hand-rolled D3D11 interop (no packages): GPU crop of the
  strip behind the bar → staging texture → BGRA. Max 20 fps with a trailing timer (the last change is never lost);
  frames whose strip didn't change are dropped before they reach WPF. Refuses to run if the yellow capture border
  can't be hidden (Windows 10) → live blur instead.
- `DockWindow`: new `Lens` layer (Border + clipped `LensWindows` canvas). The scene = wallpaper strip
  (`WallpaperGlass.SliceAsync`, full-res, off-thread, cached) + live captures of up to 3 windows behind the bar in
  z-order, GPU-blurred (`BlurEffect`, radius from Settings → Blur strength) with a bleed margin so edges don't darken,
  then bent by the shader. Re-checked on `ScreenEvents.Changed` / foreground change (150 ms debounce).
  Click-through and topmost layered overlays are ignored (LG Switch's invisible overlay was a false positive).
- Settings → "Liquid Glass refraction" (`LiquidRefraction`, default on). Only for the Liquid Glass style.

**Verified:** build 0/0, tests 24/24, live on both displays (primary screenshot + display-2 capture): frosted window
content behind the bar, bright refracted rim top-left, rounded lens corners, no capture border on the captured
window. CPU: lens on 2.11 % vs lens off 2.42 % under the same load (a streaming chat window behind each dock) → the
lens costs nothing measurable; capture delivered ~0.2 frames/s, ~1.7/s arrived and were deduplicated.
`--quit` → "Vela stopped cleanly".

**Not verified:** dragging a window across the bar (no input on display 1 while the owner may be gaming), light
style tint, top-edge docks, video playing behind the bar (worst case: 20 fps of re-render).

**Next up**
1. `Views/DockWindow.xaml.cs` `ShowLens`: tune by eye with the owner (rim width `h*0.38`, refraction `10*dpi`,
   tint 0.24 dark / 0.22 light). Maybe expose "Refraction strength" in Settings.
2. Apply the same lens to cards / picker (`GlassWindow.LiveBackdrop` path) — same scene + effect.
3. Check a top-docked bar (light direction should flip: `Light` y sign).

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
- **Tray icons (owner request) — experimental, OFF.** `Shell/TrayHost.cs` registers a second hidden top-most
  `Shell_TrayWnd` on its own thread, records Shell_NotifyIcon data (WM_COPYDATA 1) and forwards every WM_COPYDATA and
  WM_USER+ message to Explorer's tray; `TrayWidget` shows the icons in a grid and passes clicks back (v0–3 / v4
  callback formats). It worked (icons arrived, appbars kept working) **but the Windows key / Start stopped working**:
  Windows 11's Start evidently looks up its taskbar by class too and found Vela's 0×0 window. Rolled back within
  minutes; now behind `Settings.TrayHost` (default false). TaskbarController now always targets Explorer's window
  (`TrayHost.ExplorerTray()`). Next attempts, safest first: (1) read Explorer's notification area through UI
  Automation (names + Invoke; icons from the app exe) with no window of our own; (2) if keeping the host, give the
  window Explorer's taskbar rect/monitor and forward *all* messages, then test Win key, Win+X, Win+A, Start button.
- **Tray icons now work and Start keeps working** (owner tested the Windows key and Start button). Fixes taken from
  ManagedShell (Apache-2.0, RetroBar/Cairo): the host window gets Explorer's taskbar rect (a 0×0 window broke Start), a
  TrayNotifyWnd child, forwards WM_COMMAND / WM_ACTIVATEAPP as well, and strips SWP_SHOWWINDOW so it never shows.
  8 icons arrived live. `Settings.TrayHost` is now on by default as a kill switch (Settings → Dock → Background apps);
  the host only runs while a Tray tile is on a dock. Not yet verified: clicking icons (left / right / double) for every
  app, and Win+X / Win+A with the host on.
- Tray clicks: the card closed on the first click so double-clicks never reached apps (fixed: left press = click,
  second press = double-click, card stays open); Windows' own icons (no callback / owned by explorer.exe) open the
  matching Settings / Windows Security page instead. Electron apps (Claude) and iCloud never re-send their icon after
  "TaskbarCreated" (confirmed with VELA_DEBUG_TRAY=1 + a direct post), so the tray also shows **stand-ins** from
  HKCU "Control Panel/NotifyIconSettings" (running exe + icon snapshot): left-click relaunches the app (brings its
  window), no menu. `Vela.exe --tray-dump` logs every icon. Owner verified left-click opens apps.
- Tray card: app names under icons (4 columns); right-click shows the app's own menu (owner verified with
  Windhawk / LG Switch), and a small Vela menu (Open / Open settings) for built-ins and stand-ins.
- Idle CPU: Explorer's taskbar lookup (FindWindowEx + Process per call) ran every second and for every forwarded
  tray message; now cached (re-found only when the window dies). Measured with tray + badges + notifications on:
  0.27 % of a core, 66 MB.
- Stock Chart (Yahoo Finance public chart endpoint, no key; card with symbol box + 1D/5D/1M/6M/1Y) and Wallpaper
  Gallery (Unsplash access key; click a photo → SystemParametersInfo wallpaper; photographer credit + download ping).
  Temperature: this PC exposes no thermal zones to normal users (perf counters empty, MSAcpi unsupported) — needs a
  driver, left out. Do Not Disturb: no public toggle API. Now 99 of 109.
- 2026-11-18: GitHub still listed Acadbek ("asad", author of the README commits this repo started from) from a stale
  contributor cache plus orphaned commits; a default-branch switch didn't refresh it. With the owner's go-ahead the
  repo was deleted and recreated (0 stars/forks/issues to lose), main + tags v0.2.0–v0.5.1 re-pushed, all 5 releases
  re-published from a local backup (v0.5.1 latest). Contributors now: sherifrahim (+ claude co-author). The 31 local
  legacy tags (v0.1.x / v1.x) that held his commits were deleted; never push tags outside v0.2.0+.

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
