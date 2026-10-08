# PROTOTYPE: Widget and Dock look and motion

Throwaway code for [Widget and Dock look and motion](https://github.com/Gul-Sher-Khan/task-widget/issues/12). Not production code. Never merge into `main`.

The Widget (the "Fluent list" design chosen in round 1), its Capture box, Settings and the Dock, in WinUI 3 (the chosen stack). Tasks and Settings live in memory; a Capture is "interpreted" by keyword rules after a fake 2–3 s delay. Round 1's three variants are in this branch's history.

## What's decided (rounds 1–3)

- **Widget:** the "Fluent list" (round 1). Header (count, Re-sort when manual, Done view, Settings gear, collapse), Capture box on top, flat rows.
- **Priority:** alert badges: High a filled circle with "!", Medium a half-filled ring, Low an empty ring.
- **Effort:** a duration chip, `15m` / `1h` / `1h+`; the tooltip spells out Quick / Short / Long.
- **Dock:** the Capture bar, 44 px, centred 8 px under the top edge. It looks like an input ("Capture a thought…"); a click opens the Widget with the Capture box focused. Per-Priority counts sit at the right, the attention dot top-right, and "Interpreting…" with a spinner shows while a Capture is processing.
- **Settings** (gear in the Widget header): Connection (ChatGPT account and model, or API key with Groq / OpenRouter / Custom, base URL, key, model, test), Hotkey recorder, Theme, Backdrop, rows before scrolling, start with Windows, About. Sign-in, key test and the recorder are simulated.
- **Updates** (added by [Packaging, updates and signing](https://github.com/Gul-Sher-Khan/task-widget/issues/19)): Settings → About shows the version, then one update row below a hairline: "You're up to date" / "Couldn't check for updates" with **Check now**, "Checking for updates…", "Version x.y.z is available" with **What's new** and an accent **Update**, or a download bar with "Task Widget restarts to finish". Then the **Check for updates automatically** toggle (on) and its privacy caption. An available update also lights the attention dot. Check now finds 0.2.0, and the next check fails, so you can see both states.
- **Contrast themes:** follows Windows. Every Priority uses the system text colour (the badge shape carries the level), there's no backdrop, and hairlines use the text colour.

## Binding files for the implementation

- `Tokens.xaml`: colours for Dark, Light and HighContrast.
- `Motion.xaml`: every duration, distance and easing curve.
- `Styles.xaml`: the bare icon button and the plain list row.
- `Variants/WidgetA.xaml`, `Variants/SettingsPanel.xaml`, `Variants/DockCapture.xaml`, `Variants/UndoPill.xaml`, `Shell/Icons.cs`: layout, spacing, copy and icon geometry.

```
dotnet run --project prototypes/widget-look
```

## Controls

The black/yellow bar at the bottom of the screen is prototype tooling: theme (System/Light/Dark), backdrop (Mica/Mica Alt/Acrylic/Solid, all kept live when inactive), Contrast preview (an approximation of Night sky, without changing Windows), Slow-mo (every animation 5x slower), Fake Capture, attention dot, Dock/Widget, Settings, 3/9/15 Tasks, and Quit.

In the Widget: tap **Ctrl+Shift** to raise it with the Capture box focused (tap again commits and ends the session, Esc drops it); Enter adds a Capture, Shift+Enter is a newline; click a row to open its Details; tick to strike (tick again within 1.5 s to cancel); click the Priority/Effort to cycle it; double-click a Title or press E to edit; right-click or Del to delete; drag a row, or Ctrl+↑/↓, to reorder (the Re-sort icon then appears); Space ticks, Enter expands, Ctrl+Z / Ctrl+Y undo and redo.

Start-up overrides for screenshots: `LOOK_THEME` 0–2, `LOOK_BACKDROP` 0–3, `LOOK_DOCKED=1`, `LOOK_BUSY=1`, `LOOK_CONTRAST=1`, `LOOK_SETTINGS=1`, `LOOK_NOBAR=1`, `LOOK_UPDATE=1` (an update is available, Settings scrolled to About).

## Known prototype limits

- WinUI 3 windows can't be per-pixel transparent with a live backdrop, so the Dock is a rounded rectangle (DWM's 8 px corners), not a true pill.
- Widget ↔ Dock: the Widget's content fades and the window rolls up, then the Dock springs in. The backdrop itself can't fade.
- Re-interpret is only a menu entry here; its UI is out of this round.
- Contrast preview only partly recolours WinUI's own controls (e.g. the Connection tab underline); a real Windows contrast theme recolours everything.
