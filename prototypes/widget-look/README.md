# PROTOTYPE: Widget and Dock look and motion

Throwaway code for [Widget and Dock look and motion](https://github.com/Gul-Sher-Khan/task-widget/issues/12). Not production code. Never merge into `main`.

Three structurally different designs of the Widget, its Capture box and the Dock, in WinUI 3 (the chosen stack), switchable live. Tasks live in memory; a Capture is "interpreted" by keyword rules after a fake 2–3 s delay.

```
dotnet run --project prototypes/widget-look
```

## Variants

- **A · Fluent list**: header, Capture box on top, flat rows; Priority shape + Effort word chip. Dock: floating bar centred under the top edge; icons breathe while processing.
- **B · Cards**: Priority counts in the header, cards with a Priority band, pie-clock Effort; Capture box at the bottom as a composer. Dock: tab hanging from the top edge, counts in coloured discs, accent bar sweeps while processing.
- **C · Next up**: the Capture box is the top bar; the first Task is a large hero with Details and a Done button; the rest are dense one-liners with shape glyphs and Effort dots. Dock: compact cluster in the top-right corner; shapes bob while processing.

## Controls

The black/yellow bar at the bottom of the screen is prototype tooling: ◀ ▶ variant, theme (System/Light/Dark), backdrop (Mica/Mica Alt/Acrylic/Solid, all kept live when inactive), Fake Capture, attention dot, Dock/Widget, and 3/9/15 Tasks.

In the Widget: tap **Ctrl+Shift** to raise it with the Capture box focused (tap again commits and ends the session, Esc drops it); Enter adds a Capture, Shift+Enter is a newline; click a row to open its Details; tick to strike (tick again within 1.5 s to cancel); click the Priority/Effort to cycle it; double-click a Title or press E to edit; right-click or Del to delete; drag a row, or Ctrl+↑/↓, to reorder (the Re-sort icon then appears); Space ticks, Enter expands, Ctrl+Z / Ctrl+Y undo and redo.

Start-up overrides for screenshots: `LOOK_VARIANT` 0–2, `LOOK_THEME` 0–2, `LOOK_BACKDROP` 0–3, `LOOK_DOCKED=1`, `LOOK_BUSY=1`.

## Known prototype limits

- WinUI 3 windows can't be per-pixel transparent with a live backdrop, so the Dock is a rounded rectangle (DWM's 8 px corners), not a true pill. Dock B hides its top corners above the screen edge.
- Widget ↔ Dock: the Widget's content fades and the window rolls up, then the Dock springs in. The backdrop itself can't fade.
- Re-interpret is only a menu entry here; its UI is out of this round.
