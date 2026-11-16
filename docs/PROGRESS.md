# Progress log

Newest entry first. Every agent session appends one entry (see AGENTS.md → Handoff protocol).

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
