# Progress log

Newest entry first. Every agent session appends one entry (see AGENTS.md → Handoff protocol).

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
