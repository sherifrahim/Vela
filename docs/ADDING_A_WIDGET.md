# Recipes

## Add a widget

1. Pick the category file in `src/Vela/Widgets/` (or create one) and add a class:

```csharp
/// <summary>One line: what the tile shows.</summary>
public sealed class UptimeWidget : WidgetBase
{
    TextBlock _value = null!;
    protected override int TickSeconds => 30;          // 0 = no ticking

    protected override UIElement BuildContent()        // called once; build with UI.* helpers
    {
        _value = Value("—");                            // 20pt bold headline
        return V(Caption("Uptime"), _value);           // caption (11pt secondary) over value
    }

    protected override void OnTick(DateTime now) =>
        _value.Text = Fmt.Duration(TimeSpan.FromMilliseconds(Environment.TickCount64));

    protected override UIElement BuildCard() =>       // optional click card
        V(Card.Header("Uptime"), Text("Since last boot", 12, null, "Text.Secondary"));
}
```

2. Register it in `WidgetRegistry.All` (the type id is persisted — choose it carefully, never rename):
   `new("uptime", "Uptime", "System", () => new UptimeWidget()),`
3. `tools/dev.ps1`, then `Vela.exe --widgets`, add it, screenshot with `tools/screenshot.ps1`.

Conventions
- Tile height is fixed (48 DIP at size M). Design for it: 1–3 short lines, max ~200 DIP wide.
- Colours only via resource keys: `Text.Primary/Secondary/Tertiary`, `Tile.*`, `Fill.Control/Track`,
  `Accent`, `Accent.Soft`, `Sys.Red/Green/Orange/Yellow/Purple`, `OnAccent`. Use `.Fg(key)` / `.Bg(key)`.
- Subscribe to services in `OnAttach`, unsubscribe in `OnDetach`. Never start timers in a widget.
- `HasTile => false` for borderless widgets (apps, mini player); `Clickable => false` if it has no card.
- Per-instance settings: `Cfg("key", "default")` / `SetCfg("key", value)`; offer them in `AddMenuItems`.
- Typography (matches the reference): captions 11 SemiBold, values 18–22 Bold, weekday labels 9.5 Bold red.
- Coloured-outline timer tiles derive from `TimerTileBase` (2 px border in the tile's colour).

## Add a connector (needs an account/key)

1. Subclass `PollingConnector` in `src/Vela/Connectors/`: set `Id`, `Name`, `Description`, `Auth`
   (`ApiKey` → also `SecretName`, `KeyHint`, `HelpUrl`), `Interval`, `IsConnected`, and implement
   `FetchAsync()` (set your public properties, set `Error = null` and `LastUpdated` on success; throw on failure).
2. Add a lazy instance to `Services/Services.cs` and to `AllConnectors` (it then appears in Settings → Connectors).
3. Widget: subscribe to `connector.Changed` in `OnAttach`; when `!IsConnected`, the card should return
   `Card.ConnectForm(connector)`; add a "Logout" context item that calls `Secrets.Set(name, null)`.
Rules: read-only, direct to the service, keys only via `Secrets`, never touch another app's files.

## Add a style

Add a `Palette` to `Theming/Theme.cs → Palettes` and include it in `Palettes.All`.
`Backdrop` = `None` (solid bar) or `Blur`/`Acrylic` (shows `WallpaperGlass` under the tint).
`Sheen` alpha > 0 turns on the rim highlight. Check both a dark and a bright wallpaper.
