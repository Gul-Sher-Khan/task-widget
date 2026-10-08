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

## What's decided (round 4): first-run, banners, sign-in and Capture states

From [First-run, banner and Capture-state screens](https://github.com/Gul-Sher-Khan/task-widget/issues/22). Behaviour and copy come from [First run, sign-in and privacy disclosure](https://github.com/Gul-Sher-Khan/task-widget/issues/20) and [Capture flow and failure handling](https://github.com/Gul-Sher-Khan/task-widget/issues/9). Variant A ("Quiet") was picked for every surface; B and C are in commit 8acd662 of this branch's history.

- **Welcome card** (`Variants/Round4/Welcome.xaml`): plain lines, no card chrome, under the Capture box. Sign in with ChatGPT (accent, full width), the privacy line with **Details**, a hairline, then "Tap Ctrl+Shift anywhere to capture" (live hotkey) and "Collapse to the Dock with ⌄". It stays above any waiting Captures until the first sign-in. After that, an empty list shows only the hotkey line, centred and faint. This replaces "All clear.".
- **Sign-in states** (`SignInBlock.xaml`, shared by the welcome card and Settings › Connection): the button is replaced in place by a status line. Waiting shows a ring, "Waiting for your browser…" and a **Cancel** link. Failure shows an error icon, "Sign-in didn't finish" (cause in the tooltip) and a **Try again** link. If the plan isn't eligible, the plan message shows in the error colour with "See ChatGPT plans" above the button.
- **Header banner** (`Banner.xaml`): a tinted strip under the header, with hairlines above and below. It has a status icon, the message and link actions. The tint and icon use WinUI's status brushes: Attention for the newer version and recovered banners, Caution for signed out and started empty, Critical when sign-in fails from the banner. The signed-out banner runs sign-in in place: the icon becomes a ring and the message becomes the sign-in state.
- **Capture rows** (`CaptureRow.xaml`): a **waiting** row has a clock icon, the Capture text muted and the reason faint. A **failed** row has an error icon, the Capture text, the reason in the error colour (detail in the tooltip) and links: Retry · Edit · Make a Task as-is · Discard. The primary link is semibold: Make a Task as-is for "No task found in this", Retry otherwise. **Edit / Re-interpret** opens the text in place with a hint line. The pending row is the finalized one.
- **Waiting for dictation…**: the placeholder changes and a thin indeterminate bar runs along the bottom of the Capture box for up to 4 s.
- **Re-interpret**: the Capture's old Tasks fade to 45% while a "Re-interpreting…" pending row sits on top.
- **Read-only** (newer version): the tick, Priority and Effort fade to 40% and are disabled. The Capture box shows a lock, and its tooltip explains why Enter doesn't commit.
- **Settings › Connection**: ChatGPT only, with no API-key option (ADR 0003). The caption adds "Only what you capture and today's date are sent to ChatGPT. Nothing else leaves this PC." About links to the README's Privacy section.
- **Motion**: rows, banners and the welcome card fade in while sliding down 8 px (`EnterMs` 220, ease-out) and fade out over `LeaveMs` 120. The window's height animation moves everything else. Dimming takes `DimMs` 180.
- **Copy written in this round** (kept): the signed-out banner ("You're signed out of ChatGPT. New Captures wait until you sign in."), the plan-lapsed reason ("Your ChatGPT plan can't be used here"), "See ChatGPT plans", the edit hints ("Fix the text, then Enter to retry · Esc to cancel" / "…to re-interpret…"), the read-only tooltip ("Saved by a newer Task Widget. Update to add Captures.") and the failure tooltips in `Model.cs`.

**Control bar, round 4 rows:** Scene (Everyday, First run, Empty signed in, Newer version, Signed out, Recovered, Started empty, Capture states), Sign-in outcome, Next Capture outcome (ok / timeout / 429 / bad reply / zero Tasks / plan lapsed), Offline, Hold pending (the Capture states scene opens with it on), Dictation (arrives / never comes) and **Tap with empty box**. Start-up overrides: `LOOK_SCENE` 0–7, `LOOK_SIGNIN` 0–3, `LOOK_DICTATION=1`.

## Binding files for the implementation

- `Tokens.xaml`: colours for Dark, Light and HighContrast.
- `Motion.xaml`: every duration, distance and easing curve.
- `Styles.xaml`: the bare icon button and the plain list row.
- `Variants/WidgetA.xaml`, `Variants/SettingsPanel.xaml`, `Variants/DockCapture.xaml`, `Variants/UndoPill.xaml`, `Variants/Round4/*.xaml`, `Shell/Icons.cs`: layout, spacing, copy and icon geometry.
- `Shell/Helpers.cs` (`Fx`): the round-4 enter/leave and dim animations.

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
