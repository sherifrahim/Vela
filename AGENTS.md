# AGENTS.md — read this first

This file is the shared brief for **every coding agent** working here (OpenAI Codex reads `AGENTS.md`
automatically; Claude Code reads it through `CLAUDE.md`). The owner switches between agents when one runs
out of usage, so **leave the repo in a state the other agent can pick up cold**. See "Handoff protocol".

## What this project is

**Vela** is a native Windows 10/11 taskbar replacement: one floating "widget dock" on the top or bottom
screen edge holding your apps (pinned + running) and live widgets (clock, media, volume, system stats,
screen time, AI plan limits, weather, todos, notes, revenue…), in five styles (Black, White, Liquid Glass,
Frost Glass, Midnight) and three sizes.

It is the owner's **personal re-imagining of lumabar.xyz**, which is a closed-source Electron app.
Goal: same look, layout and behaviour, but native (WPF/.NET, no Chromium), fast, and lean on memory.

- `reference/lumabar/` holds lumabar's public README + screenshots **for visual reference only**.
  Never ship, embed or copy those images, its name, or its logo. Match the *design language*, not the assets.
- `docs/REFERENCE.md` — what the original does (feature inventory); the parity target.
- `docs/ROADMAP.md` — parity checklist with status. **Pick work from here.**
- `docs/PROGRESS.md` — session log + "next up". **Update it at the end of every session.**
- `docs/ARCHITECTURE.md` — how it works, and why (read before touching rendering, appbar or taskbar code).
- `docs/ADDING_A_WIDGET.md` — recipe for widgets, connectors and styles.

## Stack

- C# 13 / **.NET 10**, **WPF** (native XAML), single project `src/Vela/Vela.csproj`.
- TFM `net10.0-windows10.0.19041.0` (WinRT projections for media + radios), runs on Windows 10 1809+.
- No third-party UI libraries. Only package: `System.Management` (WMI brightness).
- Win32 via P/Invoke in `src/Vela/Interop/` (user32, shell32, dwmapi, Core Audio COM, DPAPI).

## Build / run / test

```powershell
dotnet build src/Vela/Vela.csproj                     # debug build
powershell -File tools/dev.ps1                          # quit running dock → build → start (the dev loop)
src/Vela/bin/Debug/net10.0-windows10.0.19041.0/Vela.exe # run
Vela.exe --quit            # close a running dock cleanly (restores the taskbar)
Vela.exe --widgets         # open the widget picker in the running dock
Vela.exe --settings        # open Settings in the running dock
Vela.exe --restore-taskbar # emergency: put the Windows taskbar back
Vela.exe --render <dir> [--theme glass|black|white|frost|midnight] [--public]  # dock + widgets (+ cards, island) to PNGs, off-screen; --public skips personal data, use it for docs/screenshots
Vela.exe --render <dir> --poster   # launch posters (hero, widget wall, cards) from the real widgets → docs/screenshots/vela-poster-*.png
powershell -File tools/publish.ps1                      # self-contained Release build → ./publish/win-x64
```

Visual checks (no UI automation needed):
**Prefer `--render`** (off-screen PNGs of the dock layout, the whole widget catalogue and Vela Island; it never
touches the screen or the taskbar, so it works even while the owner is gaming). Run it from a separate build output
(`dotnet build -o <dir>`) while the dock is running, because the running exe is locked.
`tools/screenshot.ps1 -Out shot.png -CropBottom 140` captures the dock strip (`-Secondary`: the other display); `tools/vela-windows.ps1`
lists Vela's windows, rects and clip regions. Logs: `%LOCALAPPDATA%\Vela\logs\vela.log`.

Tests: `dotnet test tests/Vela.Tests` (xUnit, pure logic; quit the dock first, it rebuilds Vela.exe).
`tools/smoke.ps1` runs start → render → quit → marker/log checks (refuses while an owner dock is running). "Done" means: builds with
0 errors, the dock starts, the change is visible in a screenshot, `vela.log` has no new ERROR lines, and
`Vela.exe --quit` still logs "Vela stopped cleanly".

## Ground rules (do not break these)

1. **The Windows taskbar must always come back.** Any change near `Shell/TaskbarController.cs`,
   `App.Cleanup()` or `DockWindow.Teardown()` must keep: marker file before modifying → restore on exit,
   session end, crash → `--restore-taskbar` fallback. Test with `--quit` after every run.
2. **Never kill the dock with `taskkill /F` during development** unless you then run `Vela.exe --restore-taskbar`.
   Use `Vela.exe --quit`.
3. **Performance budget:** idle CPU ≈ 0 (measured 0.16% of one core), working set ≈ 40 MB after trim.
   - All widgets share one 1 Hz `Svc.Ticker`; never add per-widget timers or busy loops.
   - Services poll only while a widget is subscribed (see the `event` add/remove pattern in
     `SystemMonitor`, `AudioEndpoint`, `PollingConnector`). Unsubscribe in `OnDetach`.
   - No blocking I/O on the UI thread. Network = `Core.Http.Client` only.
   - No continuous animations; transitions ≤ 200 ms.
4. **Privacy:** no telemetry, no Vela server. Connectors talk directly to their service. API keys go
   through `Core.Secrets` (DPAPI), never `settings.json`. Never modify another app's files (e.g. Claude
   Code / Codex credentials are read-only).
5. **Persisted ids are API.** Widget `Type` ids (`WidgetRegistry`) and settings property names are stored
   in users' `settings.json`. Don't rename them; add migrations if you must.
6. Keep the code style: small sealed classes, code-built widget UIs via `UI/UI.cs` helpers, theme colours
   only through DynamicResource keys (`Text.Primary`, `Tile.Background`, `Accent`, `Sys.Red`…), never
   hard-coded colours in widgets (brand badges excepted).

## Repo map

```
src/Vela/
  App.xaml(.cs)          startup, single instance, CLI switches, crash/exit cleanup
  Core/                  Paths, Log, Json, Settings (settings.json), Secrets (DPAPI), DataStore (todos/notes), Ticker, Http, Fmt
  Interop/               Native.cs (P/Invoke), ShellCom.cs (shell + Core Audio COM interfaces)
  Shell/                 AppBar, TaskbarController, WindowTracker, IconCache, WinActions, TaskbarPins
  Services/              Services.cs (the `Svc` locator), SystemMonitor, Audio, Media, Weather, ScreenTime, Brightness/Radios/Markets
  Connectors/            IConnector + PollingConnector; Claude, Codex, GitHub, Stripe
  Theming/               Theme.cs (palettes → brushes), Controls.xaml (control styles), WallpaperGlass.cs
  UI/                    UI.cs: builders (Text, Icon, buttons, Progress), RingGauge, Sparkline, WeatherIcon, Glyphs
  Views/                 GlassWindow (base), DockWindow, FlyoutWindow (+ Card helpers), WidgetPickerWindow, SettingsWindow
  Widgets/               WidgetBase, WidgetRegistry, and one file per category
tools/                   dev.ps1, publish.ps1, screenshot.ps1, vela-windows.ps1, crop.ps1
docs/                    ARCHITECTURE, ROADMAP, PROGRESS, REFERENCE, ADDING_A_WIDGET
reference/lumabar/       original app's public README + screenshots (reference only, do not ship)
```

## Known gotchas (learned the hard way)

- **Live blur:** the dock's glass is a separate non-layered `BackdropWindow` under each bar (composition frost,
  `Theming/CompositionFrost.cs`; accent Acrylic if composition fails). It must keep its own physical size across
  displays: it swallows `WM_DPICHANGED`, else WPF shrinks it on the 100 % display. Keep it
  directly below its dock in z-order and hide it in fullscreen / auto-hide. System backdrops (Mica/Acrylic) do NOT work
  for a dock (never active → flat grey).
- **Rendering:** windows are *layered* (`AllowsTransparency=true`) with **no** Win32 region (aliased corners);
  shapes are anti-aliased geometry clips. The non-layered "WindowChrome glass" path renders **black** with a
  region. DWM blur/acrylic is **off** (on layered windows it fills the whole rectangle). The dock's glass is a
  live-blur `BackdropWindow` (falls back to the pre-blurred wallpaper slice). Cards, picker and control panel are
  frosted glass over their own `BackdropWindow` (`GlassWindow.LiveBackdrop`). Details in ARCHITECTURE.md.
- **Backdrop windows and focus:** never `Show()` a `BackdropWindow` (it can take activation and close the card above
  it); `Place()` uses `EnsureHandle` + `SWP_NOACTIVATE`, and `FlyoutWindow` re-activates itself if a "Vela Backdrop"
  window still grabs focus. Create them outside render/layout callbacks (defer with `Dispatcher.BeginInvoke`).
- **Adaptive theme:** each dock merges a light or dark `ThemeManager.Variant` into its own `Resources`; anything
  you open from a dock (cards, previews) should adopt `DockWindow.For(anchor).AdaptiveResources`.
- **Third-party code:** OpenTaskbar (MIT) is ported in `Shell/JumpLists.cs`, `Services/GpuThermal.cs`,
  `Connectors/CursorConnector.cs` and parts of `Views/WindowPreview.cs`. WacOS is used **only at commit `2beced5`**, its
  one MIT-licensed revision (`Theming/LiquidGlass.cs` constants, `Services/GameMode.cs`); every later WacOS revision is
  GPL or "source-available, no reuse", so never copy from current WacOS. Keep the file headers and
  `THIRD_PARTY_NOTICES.md`.
- **Several displays:** `DockWindow.All` holds one dock per display; only `DockWindow.Instance` (primary) owns
  the taskbar, shell hook and windows. Use `DockWindow.For(element)` to reach the dock a widget lives in.
  Layout edits must go through `SaveWidgets()` so the other docks rebuild.
- **Line endings / glyphs:** sources are LF in git (autocrlf on). Multi-line `perl -0pi` edits silently miss on
  CRLF files: normalise with `sed -i 's/$//'` first. Writing `"E701"` through some tools stores the raw
  PUA character; both compile, but grep for the glyph name, not the escape.
- WPF SDK drops `System.IO` from implicit usings; it's re-added in `GlobalUsings.cs`. Files that import
  `System.Windows.Shapes` must write `System.IO.Path` explicitly.
- `Svc` is a global alias for `Vela.Services.Services`. Inside namespace `Vela.*`, a bare `Services`
  resolves to the *namespace*.
- PowerShell passes `$null` to a `string` P/Invoke parameter as `""`. Use `[NullString]::Value`
  (relevant for FindWindow in test scripts).
- `Glyphs` in `UI/UI.cs` contain the raw Private-Use-Area characters (Segoe Fluent Icons / MDL2). Write new
  ones as `""` escapes; some shell tools mangle the raw characters.
- The computer-use screenshot tool can't target Vela (not a Start-menu app). Use `tools/screenshot.ps1`.
- The owner's main display is 2560×1600 at 175 % plus a 1920×1080 display at 100 % on the left (negative X).
  Always reason in physical px for Win32, DIPs for WPF. `tools/screenshot.ps1` captures the primary, or the other
  display with `-Secondary`.

## Handoff protocol (Claude ⇄ Codex)

At the **end of every session** (or when you sense you're near your usage limit):
1. Make sure it builds. If you must stop mid-change, leave it compiling (stub/feature-flag the rest).
2. Append an entry to `docs/PROGRESS.md`: date, agent, what changed, what's verified vs not, and a
   precise "Next up" list (file names + what to do).
3. Tick/untick items in `docs/ROADMAP.md`.
4. Commit only if the owner asked for commits; otherwise leave the changes for them to review and say so in PROGRESS.md.

At the **start of a session**: read this file, then the latest PROGRESS.md entry, then ROADMAP.md.
