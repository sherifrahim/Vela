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
| `DockWindow` | never (WS_EX_NOACTIVATE, MA_NOACTIVATE) | full monitor width, region = the bar |
| `FlyoutWindow` (cards) | activates (text input) | closes on deactivate / Esc / second click on its widget |
| `WidgetPickerWindow`, `SettingsWindow` | normal | draggable by header |

**Transparency model (important):** `GlassWindow.RenderMode` (env `VELA_RENDER`):
- `layered` (default) — `AllowsTransparency=true`. Per-pixel alpha always works. WPF pushes the bitmap
  via UpdateLayeredWindow only when the visual tree changes (≈1 Hz for the dock), so cost is tiny.
  A rounded `SetWindowRgn` clips hit-testing to the visible shape so clicks around the bar reach the desktop.
- `chrome` (experimental) — WindowChrome glass frame + `DwmExtendFrameIntoClientArea`. Renders **black**
  as soon as a window region exists (DWM drops the glass sheet for regioned windows). Would need
  "no region + window sized exactly to the bar + DWM rounded corners" to be usable.

**Glass:** DWM accent blur/acrylic is disabled in layered mode because DWM blurs the whole window
rectangle regardless of the region (visible as black/blurred square corners). Instead the dock paints
`WallpaperGlass`: the wallpaper decoded at 200 px wide, box-blurred once on the CPU, mapped with an
`ImageBrush.Viewbox` so it lines up with the real wallpaper under the bar (handles Fill + Stretch fits;
solid-colour desktops too). Recomputed on bar move/resize and on `WM_SETTINGCHANGE(SPI_SETDESKWALLPAPER)`.
Limitation: windows dragged *behind* the dock aren't reflected. Live refraction = roadmap
(Windows.Graphics.Capture → D3D11 shader, only while something moves, like the original).

Cards / picker / settings use near-opaque fills (`Flyout.Background`), so they need no blur.

Layer order inside the bar: `Bar.Background` (wallpaper glass, glass styles only) → `Tint`
(`Bar.Background` resource = style colour) → `Sheen` (top highlight) → widgets.

## Dock placement

`DockWindow.Reposition()`:
1. Primary monitor rect + its DPI (`GetDpiForMonitor`).
2. Height (px) = (tile 48 + 12) × size scale + edge margin + 4, × DPI.
3. If "Reserve screen space": `AppBar.Reserve` (ABM_QUERYPOS/SETPOS) so maximized windows stop above
   the dock; otherwise `AppBar.Release` (still registered → we keep getting ABN_FULLSCREENAPP).
4. `SetWindowPos(HWND_TOPMOST)` to the full-width strip; the bar is centred inside; region updated.
Triggers: ABN_POSCHANGED, WM_DISPLAYCHANGE, WM_DPICHANGED, Explorer restart (`TaskbarCreated`), settings.

Size S/M/L is a `LayoutTransform` scale on the bar (0.86 / 1 / 1.16). Widgets are designed at M.

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

`Widgets/WidgetBase` = tile (48 DIP high, radius 14) + edit-mode minus badge + press animation +
context menu (custom items + Remove / Widgets… / Settings). Lifecycle: `Build()` once; `OnAttach`/`OnDetach`
on Loaded/Unloaded; `OnTick` every `TickSeconds` via the shared ticker. Cards: override `BuildCard()`.
`WidgetRegistry` is the catalogue (type id, name, category, factory, optional connector id).
Layout = ordered `settings.json → widgets[]` (`{id, type, config{}}`); reorder by dragging while the picker is open.

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
