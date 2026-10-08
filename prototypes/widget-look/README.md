# PROTOTYPE: Widget and Dock look and motion

Throwaway code for [Widget and Dock look and motion](https://github.com/Gul-Sher-Khan/task-widget/issues/12). Not production code. Never merge into `main`.

The Widget (the "Fluent list" design chosen in round 1), its Capture box, Settings and the Dock, in WinUI 3 (the chosen stack). Tasks and Settings live in memory; a Capture is "interpreted" by keyword rules after a fake 2–3 s delay. Round 1's three variants are in this branch's history.

## What's decided (rounds 1–3)

- **Widget:** the "Fluent list" (round 1). Header (count, Re-sort when manual, Done view, Settings gear, collapse), Capture box on top, flat rows.
- **Priority:** alert badges: High a filled circle with "!", Medium a half-filled ring, Low an empty ring.
- **Effort:** a duration chip, `15m` / `1h` / `1h+`; the tooltip spells out Quick / Short / Long.
- **Dock:** the Capture bar, 44 px, centred 8 px under the top edge. It looks like an input ("Capture a thought…"); a click opens the Widget with the Capture box focused. Per-Priority counts sit at the right, the attention dot top-right, and "Interpreting…" with a spinner shows while a Capture is processing.
- **Settings** (gear in the Widget header): Connection (ChatGPT account and model; the API-key option was removed in round 4, ADR 0003), Hotkey recorder, Theme, Backdrop, rows before scrolling, start with Windows, About. Sign-in and the recorder are simulated.
- **Updates** (added by [Packaging, updates and signing](https://github.com/Gul-Sher-Khan/task-widget/issues/19)): Settings → About shows the version, then one update row below a hairline: "You're up to date" / "Couldn't check for updates" with **Check now**, "Checking for updates…", "Version x.y.z is available" with **What's new** and an accent **Update**, or a download bar with "Task Widget restarts to finish". Then the **Check for updates automatically** toggle (on) and its privacy caption. An available update also lights the attention dot. Check now finds 0.2.0, and the next check fails, so you can see both states.
- **Contrast themes:** follows Windows. Every Priority uses the system text colour (the badge shape carries the level), there's no backdrop, and hairlines use the text colour.

## Round 4 (open): first-run, banners, sign-in and Capture states

For [First-run, banner and Capture-state screens](https://github.com/Gul-Sher-Khan/task-widget/issues/22). Behaviour and copy come from [First run, sign-in and privacy disclosure](https://github.com/Gul-Sher-Khan/task-widget/issues/20) and [Capture flow and failure handling](https://github.com/Gul-Sher-Khan/task-widget/issues/9); this round decides only look and motion. Three variants, switched with **Round 4 variant** on the control bar:

- **A Quiet:** plain lines, no card, link-style actions. Banner is a tinted strip under the header; a failed row has an error icon, a red reason and text links; the dictation wait changes the placeholder and runs a thin bar under the box.
- **B Cards:** the welcome card uses keycaps; banners and sign-in errors are WinUI InfoBars; a failed row is a tinted card with buttons (the primary one in the accent); a waiting row carries its reason in a chip; the dictation wait pulses the box border in the accent.
- **C Header-led:** the welcome card is centred and led by the hotkey line; the banner is a full-width band above the header; waiting and failed rows are one compact line (a failed row expands to show the reason and icon actions); the dictation wait covers the box with a mic and a bar that drains over 4 s; Re-interpret adds a sweep across the dimmed Tasks.

Shared by all three: Settings › Connection is ChatGPT only (the API-key option is gone) with the privacy line in its caption; About links to the README's Privacy section; Re-interpret dims the Capture's old Tasks to 45% and puts a "Re-interpreting…" pending row on top; read-only rows grey their tick, Priority and Effort, and the Capture box shows a lock.

New motion tokens are at the bottom of `Motion.xaml` (Enter/Leave, Dim, Shimmer, DictationWait/Pulse). `Styles.xaml`'s `Bare` button now dims to 40% when disabled (seen only in the read-only state).

**Control bar, round 4 rows:** Round 4 variant, Scene (Everyday, First run, Empty signed in, Newer version, Signed out, Recovered, Started empty, Capture states), Sign-in outcome (succeeds / didn't finish / plan not eligible), Next Capture outcome (ok / timeout / 429 / bad reply / zero Tasks / plan lapsed), Offline, Hold pending (freezes pending rows; the Capture states scene opens with it on), Dictation (arrives / never comes) and **Tap with empty box**.

Start-up overrides: `LOOK_VARIANT` 0–2, `LOOK_SCENE` 0–7, `LOOK_SIGNIN` 0–3 (idle, waiting, didn't finish, not eligible), `LOOK_DICTATION=1`.

Copy this round had to invent, for review: the signed-out banner ("You're signed out of ChatGPT. New Captures wait until you sign in."), the plan-lapsed reason ("Your ChatGPT plan can't be used here"), "See ChatGPT plans", the Edit / Re-interpret hint ("Fix the text, then Enter to retry · Esc to cancel"), the read-only Capture box tooltip, and the failure tooltips.

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
- Sign-in, Captures and "Open folder" are simulated; the outcomes come from the control bar.
- Contrast preview only partly recolours WinUI's own controls (e.g. the Connection tab underline); a real Windows contrast theme recolours everything.
