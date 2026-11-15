# Vela architecture

## Process & threads

One process, one UI (STA) thread running WPF, plus:
- `Vela.IconWorker` — STA thread extracting app icons via `IShellItemImageFactory` (never block the UI on shell COM).
- Thread-pool tasks for HTTP, log scanning (Claude/Codex), WMI brightness, file I/O.
Results are marshalled back with `Dispatcher.BeginInvoke`.

Startup (`App.OnStartup`): CLI switches → single-instance mutex → crash handlers → `Settings.Load` →
`ThemeManager.Apply` → `WallpaperGlass.Reload` → `ScreenTime.Start` → `Ticker.Start` → `new DockWindow().Show()`
→ after 8 s, GC + working-set trim.

Shutdown (`App.Cleanup`, also on SessionEnding / unhandled exception): flush data → `DockWindow.Teardown`
(deregister shell hook, remove appbar) → `TaskbarController.Restore`.

## Windows & rendering

Every surface derives from `Views/GlassWindow`:

| Window | Activation | Notes |
|---|---|---|
| `DockWindow` | never (WS_EX_NOACTIVATE, MA_NOACTIVATE) | one per display (`DockWindow.All`; `Instance` = primary); full monitor width strip |
| `FlyoutWindow` (cards) | activates (text input) | closes on deactivate / Esc / second click on its widget |
| `WidgetPickerWindow`, `SettingsWindow` | normal | draggable by header |

**Transparency model (important):** `GlassWindow.RenderMode` (env `VELA_RENDER`):
- `layered` (default) — `AllowsTransparency=true`. Per-pixel alpha always works. WPF pushes the bitmap
  via UpdateLayeredWindow only when the visual tree changes (≈1 Hz for the dock), so cost is tiny.
  **No Win32 window region** (regions are aliased = jagged corners). Fully transparent pixels of a layered
  window are click-through anyway; the visible shape is an anti-aliased `RectangleGeometry` clip on
  `RegionElement` (`GlassWindow.UpdateRegion`). The dock also draws a soft static shadow (`Shadow`) under the bar.
- `chrome` (experimental) — WindowChrome glass frame + `DwmExtendFrameIntoClientArea`. Renders **black**
  as soon as a window region exists (DWM drops the glass sheet for regioned windows). Would need
  "no region + window sized exactly to the bar + DWM rounded corners" to be usable.

**Glass:** DWM accent blur/acrylic is disabled in layered mode because DWM blurs the whole window
rectangle regardless of the region (visible as black/blurred square corners). Instead the dock paints
`WallpaperGlass`: the wallpaper decoded at 120–560 px wide (Settings → Glass → Blur strength), box-blurred once on the CPU
(+ saturation lift = macOS vibrancy), mapped with an
`ImageBrush.Viewbox` so it lines up with the real wallpaper under the bar (handles Fill + Stretch fits;
solid-colour desktops too). Recomputed on bar move/resize and on `WM_SETTINGCHANGE(SPI_SETDESKWALLPAPER)`.
Limitation: windows dragged *behind* the dock aren't reflected. Live refraction = roadmap
(Windows.Graphics.Capture → D3D11 shader, only while something moves, like the original).

Cards / picker / settings use opaque fills (`Flyout.Background`): a translucent panel that can't blur
what's behind it just shows readable text through it, which looks cheap.

**Glass setting** (`AppSettings.GlassEffect`): `auto` = style default, `off` = solid bar, `frosted` = wallpaper
glass on any style, `liquid` = frosted + specular rim (`Rim` border, `Bar.Rim` brush) + stronger top glow
(`Sheen`) and rim-gradient tile borders. `GlassTiles` turns tiles into translucent panes. `ThemeManager.GlassOn`
/ `ThemeManager.Liquid` are the resolved flags.

Layer order inside the bar: `Bar.Background` (wallpaper glass when GlassOn) → `Tint` (`Bar.Background`
resource = style colour) → `Sheen` (top highlight) → `Rim` (liquid only) → `DockPanel` [`DesktopCorner` | `Items` (DockRow)].

Borders are `Hairline` thick (a resource = exactly one physical pixel, `1 / DPI`) so they stay crisp at 175 %.

## Dock placement

`DockWindow.Reposition()`:
1. This dock's monitor (`MonitorInfo()`: primary, or the display named by `_device`) + its DPI.
2. Height (px) = ((tile 42 + 2×padding 3) × size scale + 2 hairlines + edge margin + TopGap 4) × DPI.
3. If "Reserve screen space": `AppBar.Reserve` (ABM_QUERYPOS/SETPOS) so maximized windows stop above
   the dock; otherwise `AppBar.Release` (still registered → we keep getting ABN_FULLSCREENAPP).
4. `SetWindowPos(HWND_TOPMOST)` to the full-width strip. Bar width: `BarWidth = full` stretches it edge to edge
   with the edge margin as side inset; `fit` hugs the content, centred.
5. `WindowTracker.FitMaximizedToWorkArea` re-fits windows that were maximized while the taskbar was hidden
   (they would otherwise sit under the dock). Resizable (WS_THICKFRAME) windows only, max 2 attempts per window.
Triggers: ABN_POSCHANGED, WM_DISPLAYCHANGE, WM_DPICHANGED, Explorer restart (`TaskbarCreated`), settings.

Size S/M/L is a `LayoutTransform` scale on `BarHost` (0.86 / 1 / 1.16). Widgets are designed at M.

**Item row (`Views/DockRow`)**: a Panel that lays widgets left→right; when the bar is wider than the content
the child marked `DockRow.Fill` (the apps strip) is centred on the screen, widgets before it hug the left end
and widgets after it the right end. **Dynamic fit**: `DockWindow.FitItems` scales the whole row down
(≥ 60 %) when `NaturalWidth` exceeds the screen, back to 100 % when it fits.

**Multiple displays** (`Displays = all`): the primary dock (`IsPrimary`) owns the shell hook, the taskbar and
the ticker-driven enforcement, and opens/closes one secondary `DockWindow(device)` per extra display
(`SyncDisplays`, re-run on WM_DISPLAYCHANGE). All docks show the same layout; every layout change goes
through `SaveWidgets()` → `Settings.Changed("widgets")` → the *other* docks rebuild. Cards anchor to the
dock that contains their widget (`DockWindow.For(element)`).

## Windows taskbar

`Shell/TaskbarController`: modes `hidden` (auto-hide state + `ShowWindow(SW_HIDE)` on
`Shell_TrayWnd` and every `Shell_SecondaryTrayWnd`, re-enforced every 3 s because Explorer re-shows it),
`autohide`, `keep`. The original state is written to `%LOCALAPPDATA%\Vela\taskbar-restore.json`
*before* the first change; the next launch trusts the marker over the current (modified) state.

## Fullscreen

`DockWindow.CheckFullscreen()` on ABN_FULLSCREENAPP and on every foreground change:
fullscreen if `SHQueryUserNotificationState` ∈ {busy, D3D fullscreen, presentation} or the foreground
window has no caption and covers the primary monitor. Then the dock hides and `Svc.Ticker.Paused = true`
(no drawing, no polling). Screen time keeps its own timer so games still count.

## Apps (taskbar replacement)

`Shell/WindowTracker`: `RegisterShellHookWindow` on the dock HWND → HSHELL_* messages (created,
destroyed, activated, redraw, flash) → debounced `EnumWindows` refresh. Eligibility = visible, not cloaked
(hides other virtual desktops / suspended UWP), no owner unless WS_EX_APPWINDOW, not toolwindow, has a title.
Grouping key = AppUserModelID (via `SHGetPropertyStoreForWindow`) else exe path.
Pins: imported once from the user's real taskbar pins (`…\User Pinned\TaskBar\*.lnk`), then stored in
`settings.json`. Matching pin ↔ window by target exe path or AUMID.
Icons: `IconCache` (`shell:AppsFolder\<AUMID>` for Store apps, exe/lnk path otherwise), 64 px, alpha-correct.

## Widgets

`Widgets/WidgetBase` = tile (42 DIP high, radius 12, hairline border) + edit-mode minus badge + press animation +
context menu (custom items + Remove / Widgets… / Settings). Lifecycle: `Build()` once; `OnAttach`/`OnDetach`
on Loaded/Unloaded; `OnTick` every `TickSeconds` via the shared ticker. Cards: override `BuildCard()`.
`WidgetRegistry` is the catalogue (type id, name, category, factory, optional connector id).
Layout = ordered `settings.json → widgets[]` (`{id, type, config{}}`). Reorder: drag any widget sideways
(> 7 DIP; sliders / text boxes excluded) at any time; app icons reorder pins inside the strip (dropping a running
app among the pins pins it). **Stacks** (`StackWidget`, type `stack`, hidden from the picker): children are
widget instances serialised into the stack's `config.items`; scroll to flip, optional auto-rotate; created via a
widget's menu → "Stack with next widget", undone via "Unstack". Children report config edits through
`WidgetBase.ConfigChanged` so their settings persist.

**Cards** (`FlyoutWindow`) are positioned through WPF `Left/Top` in DIPs (a raw SetWindowPos gets overwritten
by WPF's size-to-content pass, which used to drop cards mid-screen), centred over the widget, just above
the bar edge, clamped to that display. `FlyoutWindow.Navigate(content)` swaps a card's content in place
(Control Centre → Wi-Fi list). Memory is trimmed 4 s after the last card closes.

**Magnification** (`AppsWidget`): per-frame (`CompositionTarget.Rendering`) easing toward a cosine falloff
around the pointer measured in un-magnified slot units; slots widen so neighbours make room. The hook is
removed as soon as icons settle, so it costs nothing at rest.

## Services & connectors

`Services/Services.cs` (`Svc`): lazy singletons; nothing runs until a widget asks. Polling services expose
an `event` whose `add`/`remove` start/stop polling (zero subscribers = zero work).

Connectors (`Connectors/`) derive from `PollingConnector` (interval, error mapping, UI-thread `Changed`):
- **Claude** — limits: reads `~/.claude/.credentials.json` (or `$CLAUDE_CONFIG_DIR`) read-only, calls the
  OAuth usage endpoint Claude Code's `/usage` uses (undocumented; parsed defensively: any object with
  `utilization` + `resets_at`). Tokens: incremental scan of `~/.claude/projects/**/*.jsonl` (only new bytes),
  deduped by message id + request id. *On this PC Claude Code has no credentials file (the desktop app
  holds the sign-in), so only tokens show — expected.*
- **Codex (ChatGPT)** — offline: last `rate_limits` in the newest `~/.codex/sessions/YYYY/MM/DD/*.jsonl`.
- **GitHub** — classic PAT (notifications, read:user).
- **Stripe** — restricted key; sums `net` of charge/payment/refund balance transactions, 30 days + 14-day series.
Keys live in `secrets.json` encrypted with DPAPI (`Core/Secrets`).

Device services (`Services/Connectivity.cs`, `SystemMonitor.cs`), all on-demand:
- **WifiService** — `Windows.Devices.WiFi`: scan, connected SSID, connect (saved profile first, then key),
  disconnect. Win11 24H2+ needs "Let desktop apps access your location" for scan results; the card says so.
- **BluetoothService** — association endpoints: paired devices (+ connected flag, battery % from the device
  node `{104EA319-…} 2` in the same container), live discovery (DeviceWatcher, only while the card is open),
  `PairAsync` / `UnpairAsync`. Connecting an already-paired audio device has no public API → Settings link.
- **QuickSettingsService** — power mode via `PowerSetActiveOverlayScheme` (powrprof, same as Settings),
  battery saver status. Airplane mode = all radios (`RadioService.SetAirplaneAsync`).
- **GpuMonitor** — PDH counters `GPU Engine(*engtype_3D)` + `GPU Adapter Memory(*)Dedicated Usage`, 2 s.
- Desktop peek: undocumented `DwmpActivateLivePreview` (dwmapi #113), wrapped; click = Win+D.

## Files on disk (`%LOCALAPPDATA%\Vela`)

`settings.json` (layout, style, pins, weather city), `data.json` (todos, notes), `secrets.json` (DPAPI),
`screentime/yyyy-MM-dd.json`, `taskbar-restore.json` (only while the taskbar is modified), `logs/vela.log` (1 MB rolling).
All JSON is written atomically (temp file + move).

## Memory notes

Measured (Debug, 8 widgets, 175 % DPI): working set ≈ 37 MB after trim, private ≈ 114 MB, idle CPU ≈ 0.16 %
of one core. Levers already pulled: workstation non-concurrent GC + `GCConserveMemory=7`, lazy services,
icon decode at 64 px, album art decoded at 160 px, wallpaper at 200 px, previews disposed when the picker
closes, working-set trim after startup / closing big windows. Next levers: ReadyToRun publish (startup),
avoid `System.Management` load unless Brightness is on the dock (already lazy), trimming WinRT projections.
