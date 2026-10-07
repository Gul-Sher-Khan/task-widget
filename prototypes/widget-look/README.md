# PROTOTYPE: Widget and Dock look and motion

Throwaway code for [Widget and Dock look and motion](https://github.com/Gul-Sher-Khan/task-widget/issues/12). Not production code. Never merge into `main`.

The Widget (the "Fluent list" design chosen in round 1), its Capture box, Settings and the Dock, in WinUI 3 (the chosen stack). Tasks and Settings live in memory; a Capture is "interpreted" by keyword rules after a fake 2–3 s delay. Round 1's three variants are in this branch's history.

## Round 2 switches

- **Priority icons:** Signal bars, Chevrons, Alert badges (each level has its own shape, for contrast themes).
- **Effort:** Pie clock, Duration (15m / 1h / 1h+), Symbols (bolt / clock / hourglass).
- **Dock:** Status pill (counts + done-today ring), Next up (top Task, tickable from the Dock, +N more), Capture bar (click to capture; counts at the right).
- **Settings** (gear in the Widget header): Connection (ChatGPT account and model, or API key with Groq / OpenRouter / Custom, base URL, key, model, test), Hotkey recorder, Theme, Backdrop, rows before scrolling, start with Windows, About. Sign-in, key test and the recorder are simulated.

```
dotnet run --project prototypes/widget-look
```

## Controls

The black/yellow bar at the bottom of the screen is prototype tooling: Dock design, Priority icons, Effort style, theme (System/Light/Dark), backdrop (Mica/Mica Alt/Acrylic/Solid, all kept live when inactive), Fake Capture, attention dot, Dock/Widget, Settings, 3/9/15 Tasks, and Quit.

In the Widget: tap **Ctrl+Shift** to raise it with the Capture box focused (tap again commits and ends the session, Esc drops it); Enter adds a Capture, Shift+Enter is a newline; click a row to open its Details; tick to strike (tick again within 1.5 s to cancel); click the Priority/Effort to cycle it; double-click a Title or press E to edit; right-click or Del to delete; drag a row, or Ctrl+↑/↓, to reorder (the Re-sort icon then appears); Space ticks, Enter expands, Ctrl+Z / Ctrl+Y undo and redo.

Start-up overrides for screenshots: `LOOK_DOCK` / `LOOK_PRI` / `LOOK_EFF` 0–2, `LOOK_THEME` 0–2, `LOOK_BACKDROP` 0–3, `LOOK_DOCKED=1`, `LOOK_BUSY=1`, `LOOK_SETTINGS=1`, `LOOK_NOBAR=1`.

## Known prototype limits

- WinUI 3 windows can't be per-pixel transparent with a live backdrop, so the Dock is a rounded rectangle (DWM's 8 px corners), not a true pill.
- Widget ↔ Dock: the Widget's content fades and the window rolls up, then the Dock springs in. The backdrop itself can't fade.
- Re-interpret is only a menu entry here; its UI is out of this round.
