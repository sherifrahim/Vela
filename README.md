# Vela

A native widget dock for Windows 10 and 11: your apps and live widgets in one glassy bar on the bottom
(or top) edge of the screen. While it runs, the Windows taskbar steps aside; quit Vela and it comes back
exactly as it was.

Personal project, inspired by the look and feel of lumabar.xyz, rebuilt natively in C#/WPF on .NET 10:
no Electron, no Chromium. Idle cost: ~0 % CPU, ~40 MB working set.

## What's in it

- **Apps:** Start, your pinned taskbar apps and every open window, grouped per app, with running dots,
  attention highlight, launch bounce, and a pin/unpin/close menu.
- **41 widgets:** clocks, day/month/year progress, screen time, pomodoro, timer, stopwatch, todos, notes,
  currency, CPU/RAM/network, four battery styles, Wi-Fi/Bluetooth toggles, brightness, theme toggle,
  notifications, show desktop, storage, recycle bin, now playing (3 sizes), speakers, microphone,
  Claude and ChatGPT/Codex plan limits, Stripe revenue, crypto, weather, sunrise/sunset, GitHub.
- **Five styles** (Black, White, Liquid Glass, Frost Glass, Midnight), eight accent colours, three sizes.
- **Widget picker:** right-click the dock → *Widgets…*, then click to add or remove; drag to reorder while it's open.
- **Private:** no account, no server, no telemetry. Keys are encrypted with Windows DPAPI on this PC.

## Run it

Requires the .NET 10 SDK to build (or use `tools/publish.ps1` for a self-contained copy).

```powershell
dotnet build src/Vela/Vela.csproj
src\Vela\bin\Debug\net10.0-windows10.0.19041.0\Vela.exe
```

| Command | What it does |
|---|---|
| `Vela.exe --quit` | close the running dock (restores the taskbar) |
| `Vela.exe --widgets` / `--settings` | open the picker / settings |
| `Vela.exe --restore-taskbar` | emergency: bring the Windows taskbar back |

Data lives in `%LOCALAPPDATA%\Vela` (settings, todos/notes, screen time, encrypted keys, logs).

## For contributors and agents

Start with [AGENTS.md](AGENTS.md), then [docs/PROGRESS.md](docs/PROGRESS.md) and [docs/ROADMAP.md](docs/ROADMAP.md).
Architecture: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md). Recipes: [docs/ADDING_A_WIDGET.md](docs/ADDING_A_WIDGET.md).
