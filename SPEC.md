# Task Widget v1

Build spec for Task Widget, a lean Windows 11 desktop task widget fed by dictated or typed Captures, released open-source on GitHub. It was charted in [Task Widget: chart to a build-ready spec](https://github.com/Gul-Sher-Khan/task-widget/issues/1). Each decision below was made in one of that map's tickets, and the map links them all.

**Binding sources.** Read these before building. They win over any summary here.

- **The finalized look prototype:** [`prototypes/widget-look` @ `2c94158`](https://github.com/Gul-Sher-Khan/task-widget/tree/2c94158/prototypes/widget-look) on `prototype/widget-look`. Its README lists the binding files. Reproduce it exactly: layout, spacing, colours, typography, copy, motion timings and easing, and states. Copy `Tokens.xaml`, `Motion.xaml` and `Styles.xaml` unedited. If something in it can't be built as-is, stop and ask (see `AGENTS.md`, Prototype fidelity).
- **The Capture contract v4:** [`contract.mjs` @ `79e2b65`](https://github.com/Gul-Sher-Khan/task-widget/blob/79e2b65d6f01a5155d4ae744bbd7e0f8486f29ad/prototypes/capture-contract/contract.mjs). Copy the schema, instructions text and tolerant parse word for word. Don't reword it.
- **ADRs:** [0001 WinUI 3 + C# NativeAOT stack](docs/adr/0001-winui3-nativeaot-stack.md), [0002 DPAPI file for credentials](docs/adr/0002-dpapi-file-for-credentials.md), [0003 ChatGPT is the only Connection](docs/adr/0003-chatgpt-only-connection.md), [0004 JSON file storage](docs/adr/0004-json-file-storage.md), [0005 Inno Setup installer](docs/adr/0005-inno-setup-installer.md).
- **Vocabulary:** [`GLOSSARY.md`](GLOSSARY.md). Capture, Capture box, Connection, Task, Title, Details, Priority, Effort, Done, Deleted, Re-interpret, Widget, Dock and Home monitor mean exactly what it says.

## Problem Statement

I think of things to do all day, mostly while I'm in the middle of something else. Writing them down properly means switching to a to-do app, typing a title, picking a priority, and getting back to work. So I skip it, and things get lost. I already dictate with Wispr Flow, but nothing turns a rambling spoken thought into tidy, ranked tasks.

My machine is also full. I run about ten heavy apps (Cursor, VS Code, Edge, Opera, Teams, Slack, ChatGPT, Claude, Wispr Flow, Time Doctor) at around 82% memory. Another Electron app with a 300 MB WebView is the last thing I want running all day. Whatever sits on my desktop must cost almost nothing while idle, and still look good and move smoothly.

## Solution

Task Widget is a small native Windows 11 app with two forms. The **Widget** is a window pinned to the desktop layer that lists my Tasks, ranked so important, quick Tasks come first. The **Dock** is a slim Capture bar under the top screen edge. I tap Ctrl+Shift anywhere, say or type a thought into the Capture box, and tap Ctrl+Shift again. The Widget docks right away, and a few seconds later the Capture becomes one or more Tasks. Each Task has a verb-first Title, short Details, a Priority and an Effort, chosen by ChatGPT through my own ChatGPT account (Sign in with ChatGPT).

Nothing is ever lost. If ChatGPT can't be reached, I'm signed out or the reply is useless, the Capture stays in the Widget as a row I can retry, edit, turn into a Task as-is or discard. I can tick, edit, reorder, delete and undo everything from the keyboard or the mouse. Data stays on my PC in plain JSON. Only the Capture text and today's date go to ChatGPT.

It is one process, about 55 MB private when docked, with no background updater. It installs per-user without admin rights, and other people can install it from GitHub Releases or winget and connect their own ChatGPT account.

## User Stories

### Capturing

1. As a user, I want to tap Ctrl+Shift anywhere to raise the Widget with the Capture box focused, so that I can capture a thought without reaching for the mouse.
2. As a user, I want the hotkey to fire only on a clean tap (press both, release both, no other key in between, under 1 s), so that my Ctrl+Shift+X shortcuts in other apps keep working.
3. As a user, I want the Widget focused within 100 ms of the tap, so that I can start dictating straight away.
4. As a Wispr Flow user, I want to dictate into the Capture box with Flow's own unchanged shortcut, so that dictating elsewhere never opens the Widget.
5. As a user, I want a second Ctrl+Shift tap to commit whatever is in the Capture box and dock the Widget, so that capturing is two taps and a sentence.
6. As a dictating user, I want a Ctrl+Shift tap on an empty Capture box to show "Waiting for dictation…" for up to 4 s and commit as soon as the text lands, so that I can tap before Flow has pasted and the text doesn't end up in another app.
7. As a user, I want the Widget to dock with no Capture if no text arrives within 4 s, so that an accidental tap costs nothing.
8. As a user, I want Enter to commit and keep the Widget open with an empty box, so that I can capture several thoughts in a row.
9. As a user, I want Shift+Enter to insert a new line, so that I can write a longer Capture.
10. As a user, I want Esc or clicking another window to dock the Widget without committing, so that I can back out at any time.
11. As a user, I want uncommitted text kept as a draft that survives docking, restarts and crashes, so that a half-finished thought is never lost.
12. As a user, I want a small × at the right end of the Capture box while it holds text, so that I can clear a stale draft in one click.
13. As a user, I want Ctrl+Z to bring back a draft I cleared with ×, so that a slip of the mouse doesn't cost me the text.
14. As a Wispr Flow user, I want the Widget to stay open when Flow's own window takes focus, so that the Flow Bar can't dock the Widget mid-dictation.
15. As a user, I want hovering the Capture box to tell me I can type or dictate, and that Enter adds and Shift+Enter is a new line, so that I learn the input without a tour.
16. As a user, I want to click the Dock to open the Widget with the Capture box focused, so that the mouse path is as short as the keyboard path.
17. As a user, I want to rebind the hotkey in Settings to another modifier tap or a chord, so that I can avoid a clash with my keyboard-language toggle or another app.
18. As a user with two keyboard languages, I want Settings to warn me that Ctrl+Shift is the Windows language toggle, so that I understand why I might rebind it.
19. As a user, I want each committed Capture to appear at once as an "Interpreting…" row at the top of the list, so that I know it was taken.
20. As a user, I want several Captures to be interpreted at the same time, each with its own row, so that a slow one doesn't hold up the others.
21. As a user, I want the Dock to show a spinner and "Interpreting…" while any Capture is processing, so that I can see work is going on while the Widget is docked.
22. As a user, I want a Capture's Tasks to replace its pending row and settle into their ranked places, so that new Tasks land where they belong.

### Interpretation

23. As a user, I want one Capture split into one Task per action I'd tick off on its own, so that "email Sarah the invoice, refactor auth before Friday, buy milk" gives three Tasks.
24. As a user, I want steps I say out loud kept in one Task with the steps in its Details, so that my list doesn't fill with sub-steps.
25. As a user, I want one action with several objects kept as one Task ("Buy milk, eggs and bread"), so that errands stay together.
26. As a user, I want thinking or deciding to count as a Task ("Decide whether to move to Postgres"), so that decisions I owe aren't dropped.
27. As a user, I want a meeting with something to prepare to give only the prep Task, with the meeting in its Details, so that I don't get a Task for just attending.
28. As a user, I want chatter, feelings and plain facts to give no Task, so that my list holds only actions.
29. As a user, I want Titles that start with a verb, fit in 60 characters, keep my own words and my own voice ("my", never "your"), so that each Task reads like I wrote it.
30. As a user who dictates in Roman Urdu, I want Tasks in the language I spoke, so that the app doesn't translate my thoughts.
31. As a user, I want Details to hold only context I said (who, when, where, why, amounts) and never invented advice or steps, so that I can trust them.
32. As a user, I want relative dates turned into a short date ("Before Fri 9 Oct"), and ambiguous ones like "next Saturday" kept in my words, so that Details stay correct after today.
33. As a user, I want Priority to default to Medium and become High only for a reason I gave (a deadline within 3 days, someone waiting, a stated consequence), so that my list isn't all red.
34. As a user, I want my own urgency words ("urgent", "no rush", "whenever") to decide Priority, so that the app doesn't overrule me.
35. As a user, I want Effort estimated as Quick (15 min or less), Short (up to an hour) or Long (more than an hour), so that quick wins rise to the top.
36. As a user, I want Tasks from one Capture kept in the order I said them when they rank equal, so that the list follows my train of thought.

### The Widget and ranking

37. As a user, I want Tasks ranked by Priority, then Effort, then oldest first, so that important, quick Tasks come first.
38. As a user, I want each row to show a round tick, the Title (up to 2 lines), a Details chevron, an Effort chip (`15m` / `1h` / `1h+`) and a Priority badge, so that I can scan the list.
39. As a user, I want Priority shown as alert badges (High a filled circle with "!", Medium a half-filled ring, Low an empty ring) in fixed colours, so that I can tell levels apart at a glance and by shape.
40. As a user, I want the Effort chip's tooltip to spell out "Quick · 15 min or less" and the others, so that I can learn the chip.
41. As a user, I want to drag a whole row to any position, with the row lifting and the others sliding apart, so that I can put a Task where I want it.
42. As a keyboard user, I want Ctrl+↑/↓ to move the selected Task, so that reordering doesn't need the mouse.
43. As a user, I want dragging to change only position, never Priority or Effort, so that reordering doesn't rewrite my Tasks.
44. As a user, I want new and edited Tasks placed just before the first Task the ranking puts strictly below them, so that my manual order survives new Tasks.
45. As a user, I want a Re-sort icon in the header, shown only while manual positions exist, so that I can go back to pure ranking in one click.
46. As a user, I want the Widget to grow with the list up to my "rows before scrolling" (4–15, default 8) and then scroll, so that it never covers half my screen and never hides Tasks.
47. As a user, I want the header to show the open Task count, so that I know how much is on my plate.
48. As a user, I want to click a row to open its Details as an accordion (one open at a time), so that the list stays compact.
49. As a user, I want rows with empty Details to show no chevron, so that I don't open empty rows.

### Working with Tasks

50. As a user, I want to tick a Task to strike it through for 1.5 s and then animate it out, so that completing feels good and I can catch a mis-tick.
51. As a user, I want a second tick within 1.5 s to cancel the completion, so that a mis-click costs nothing.
52. As a user, I want a Done view, toggled from the header, listing the last 90 days of Done Tasks newest first, so that I can see what I've finished.
53. As a user, I want to un-complete a Task from the Done view and have it placed back by the ranking, so that a Task I reopened lands sensibly.
54. As a user, I want to delete a Task from the right-click menu or with Del, so that wrong Tasks go away without a button cluttering every row.
55. As a user, I want Deleted Tasks never shown again anywhere, Done view included, so that deleting means gone.
56. As a user, I want a 5 s Undo pill after completing or deleting, so that I can undo with the mouse.
57. As a user, I want Ctrl+Z / Ctrl+Y to undo and redo every action since the Widget opened (complete, delete, edit, move, Re-sort, Re-interpret), so that I can experiment safely.
58. As a user, I want to double-click a Title or press E to edit it in place, so that fixing a Title is quick.
59. As a user, I want to click the Priority badge or Effort chip to cycle it, so that changing either is one click.
60. As a user, I want to edit Details inside the expanded row, so that I can add context.
61. As a user, I want my edits to Priority and Effort to always win over ChatGPT's, so that the app never overrides me.
62. As a user, I want an edited Task re-placed by the ranking, so that a Task I made High moves up.
63. As a user, I want to Re-interpret a Capture from any of its Tasks, fixing the text in place first, so that a mis-heard dictation can be redone.
64. As a user, I want the Capture's old not-Done Tasks dimmed to 45% under a "Re-interpreting…" row while it re-runs, so that I can see what will be replaced.
65. As a user, I want Re-interpret to replace only that Capture's not-Done Tasks, in one undoable step, so that finished work is kept.
66. As a user, I want a failed Re-interpret to leave the old Tasks untouched and show a "Re-interpret failed" row with my corrected text, so that nothing is lost.
67. As a keyboard user, I want arrows to move the selection, Space to tick, Enter to expand, E to edit, Del to delete, Ctrl+↑/↓ to move and Ctrl+Z/Y to undo and redo, so that I never need the mouse once the Widget is raised.

### Failures and waiting

68. As a user, I want a Capture that fails to stay as a failed row with its text, a one-line reason and Retry · Edit · Make a Task as-is · Discard, so that a Capture is never lost.
69. As a user, I want failed rows to survive restarts, so that I can deal with them later.
70. As a user, I want Captures I make while offline to wait as "Waiting for connection" rows and run on their own when the network returns, so that I don't have to retry them.
71. As a user, I want a Capture ChatGPT couldn't reach to show "Couldn't reach ChatGPT" with Retry, so that I know to try again.
72. As a user, I want a rate-limited Capture to show "Rate-limited by ChatGPT" and not retry on its own, so that the app doesn't burn my plan's quota blind.
73. As a user, I want a Capture with a useless reply to show "Couldn't understand the reply" with Retry, so that I can re-run it.
74. As a user, I want a Capture with no action in it to show "No task found in this", with Make a Task as-is as the main action, so that I can still keep it as a Task.
75. As a user, I want Make a Task as-is to use my raw text as the Title, so that I can keep a Capture the model didn't understand.
76. As a user, I want Edit on a failed row to open the text in place and re-run it on Enter, so that I can fix a mis-heard dictation.
77. As a user, I want the reason's tooltip to give more detail, so that I can tell what went wrong.
78. As a user, I want a Capture whose plan stopped being eligible to fail with "Your ChatGPT plan can't be used here", without signing me out, so that a lapsed plan is clear and nothing is lost.
79. As a user, I want the Dock's attention dot lit while anything needs me (a failed row, signed out, no Connection yet, an update available), so that I notice while docked.
80. As a user, I want the dot left off for things that sort themselves out (offline waiting, a new Task), so that it only means "act".

### First run, sign-in and privacy

81. As a new user, I want the Widget to open expanded on first launch with a welcome in the empty list, so that I see what to do without a wizard or tour.
82. As a new user, I want the welcome to show "Sign in with ChatGPT", a privacy line with Details, the live hotkey ("Tap Ctrl+Shift anywhere to capture") and "Collapse to the Dock with ⌄", so that I learn the whole app in four lines.
83. As a new user, I want to capture before signing in, with my Captures waiting as "Connect your ChatGPT account" rows that run once I'm signed in, so that I can start right away.
84. As a user, I want sign-in to open my browser, and the Widget to show "Waiting for your browser…" with Cancel, so that I know what's happening.
85. As a user, I want the browser tab to say "Signed in, you can close this tab" and the Widget to come to the front, so that the hand-off back is obvious.
86. As a user, I want a cancelled, denied, timed-out (5 min) or broken sign-in to show "Sign-in didn't finish" with Try again and the cause in a tooltip, without a dialog, so that failing to sign in is calm.
87. As a user, I want sign-in to start from the welcome card, the signed-out banner or Settings › Connection and share one state, so that I can't start two sign-ins.
88. As a user on an ineligible plan, I want "This ChatGPT plan can't be used in Task Widget. Go, Plus or Pro works." with "See ChatGPT plans", and to stay signed out, so that I know why it won't work.
89. As a user, I want the welcome gone for good after my first sign-in, with an empty list showing only the hotkey line, so that the Widget stays clean.
90. As a user, I want to sign out from Settings without a confirmation, keeping my Tasks and Captures, so that signing out is safe.
91. As a signed-out user, I want a banner "You're signed out of ChatGPT. New Captures wait until you sign in." with Sign in, and my Captures waiting, so that nothing is lost while I'm out.
92. As a user with two ChatGPT accounts, I want sign out then sign in to show the account picker, so that I can switch accounts.
93. As a user whose token refresh failed, I want re-sign-in to go back to the same account, so that I don't pick the wrong one by accident.
94. As a privacy-minded user, I want to be told on the welcome card and in Settings › Connection that "Only what you capture and today's date are sent to ChatGPT. Nothing else leaves this PC.", so that I know what leaves my machine.
95. As a privacy-minded user, I want the README's Privacy section (linked from the welcome card and About) to give the full statement, including `store: false`, plan usage and the daily GitHub update check, so that I can check the details.
96. As a user, I want Captures billed to my own ChatGPT plan, with no API key and nothing charged by the app, so that using it costs nothing extra.

### Settings

97. As a user, I want Settings as a panel inside the Widget, opened from the header gear, so that there's no separate window.
98. As a user, I want Settings › Connection to show my account and plan, a Model dropdown with Automatic first, and Sign out, so that I can see and change how Captures are interpreted.
99. As a user, I want Automatic to pick the first available model from a shipped preference list, and my chosen model to fall back to Automatic silently if it disappears, so that model churn never breaks Captures.
100. As a user, I want to choose Theme (System / Light / Dark), so that I can override the system.
101. As a user, I want to choose the backdrop (Mica / Mica Alt / Acrylic / Solid) and have it stay live when the Widget isn't focused, so that the Widget looks the same focused or not.
102. As a user with transparency effects off or on battery saver, I want the Widget to fall back to Solid and return to my choice later without changing my setting, with a note in Settings, so that I understand the change.
103. As a user, I want to set "rows before scrolling" and "Start with Windows", so that the Widget fits my screen and starts when I sign in.
104. As a user, I want About to show the version, a GitHub link and a link to the README's Privacy section, so that I can find the source and the policy.

### Look, theming and accessibility

105. As a user, I want the Widget to follow Windows dark/light and my accent colour, with the accent used only on interactive bits (focus ring, selection, Capture box border and caret, attention dot), so that it fits my desktop without recolouring Priority.
106. As a user with a contrast theme, I want system colours, no backdrop, and Priority carried by badge shape, so that the Widget is readable.
107. As a user, I want every animation to use the prototype's timings and easing, so that the app feels like the prototype I approved.
108. As a screen-reader user, I want every control named and every row's Title, Priority and Effort announced, so that I can use the Widget without seeing it.

### Desktop placement

109. As a user, I want the Widget to sit at the bottom of the z-order, under my other windows, so that it lives on the desktop and never covers my work.
110. As a user, I want the Widget to stay visible after Win+D, so that "show desktop" shows my Tasks.
111. As a user, I want the collapse control (⌄) in the header to turn the Widget into the Dock, and the last state remembered across restarts, so that the app opens the way I left it.
112. As a user, I want the Dock as a 44 px Capture bar, centred 8 px under the top edge, topmost, not reserving screen space, so that it's always reachable and never shrinks my maximised windows.
113. As a user, I want the Dock to show per-Priority badges with counts of open Tasks, hiding levels with none, so that I know my load at a glance.
114. As a user, I want the Dock hidden while a full-screen app, game or presentation runs on the home monitor, with the hotkey still raising the Widget over it, so that I can still capture during a presentation.
115. As a multi-monitor user, I want to drag the Widget by its header to another monitor to make it the home monitor, snapping to its top-right anchor, so that I choose where it lives.
116. As a multi-monitor user, I want the Widget and Dock always on the same monitor, and the hotkey to raise the Widget on the home monitor, so that I always know where it appears.
117. As a laptop user, I want the Widget and Dock to stand in on the main display while my home monitor is unplugged and return when it's back, so that docking and undocking my laptop just works.
118. As a user, I want the windows re-anchored after any resolution, scale or taskbar change, with no blur at any DPI, so that the Widget always looks sharp and in place.
119. As a user with a top or auto-hide taskbar, I want the Dock 8 px under the taskbar or screen edge, so that the two don't overlap.
120. As a user, I want a second launch to raise the running Widget, so that I never get two copies.

### Storage and durability

121. As a user, I want my Tasks, Captures and draft saved within moments of every change and before a Capture is sent, so that a crash or power cut loses nothing.
122. As a user, I want a damaged task file restored from its backup with a banner ("Your task list was damaged and has been restored from a backup (saved 14:02).") with Open folder and Dismiss, so that I know what happened.
123. As a user, I want a fresh start with "Your task list couldn't be read, so Task Widget started fresh. The old file was kept." if both files are bad, with the old file kept, so that nothing is deleted behind my back.
124. As a user who rolled back to an older version, I want the Widget read-only with "These tasks were saved by a newer Task Widget. Update to make changes." and Get update, so that an old version can't damage newer data.
125. As a user in read-only mode, I want typing a draft to still work, with the Capture box's lock tooltip explaining why Enter doesn't commit, so that I can still jot something down.
126. As a user, I want Done Tasks older than 90 days, and Deleted Tasks after 30, moved out of the live file, so that the app stays lean after years of use.
127. As a user, I want my data in plain files under `%LOCALAPPDATA%\TaskWidget\`, documented in the README, so that I can back it up myself.

### Install, update, uninstall

128. As a user, I want a per-user installer for my architecture (x64 or ARM64) under 20 MB with no admin rights, so that installing is quick and doesn't need IT.
129. As a Windows 10 user, I want the installer to refuse with "requires Windows 11", so that I don't install something that won't run.
130. As a user, I want "Start Task Widget when I sign in" checked on the installer's last page, and turning it off in Task Manager to stay off, so that startup does what I chose.
131. As a user, I want the app to check GitHub Releases once a day and tell me in Settings › About (with the attention dot) when a new version is out, so that I hear about updates without nagging.
132. As a user, I want Update to download the installer, run it silently and restart the app with my Tasks and draft intact, so that updating is one click.
133. As a user, I want to turn off automatic update checks and use Check now instead, so that the app makes no network calls I don't want.
134. As a user, I want the uninstaller to ask "Also delete your tasks and settings?" with keeping them as the default, and a silent uninstall to keep them, so that uninstalling never deletes my data by surprise.
135. As a winget user, I want to install Task Widget with winget, so that I can manage it like my other apps.
136. As a user of an unsigned v1, I want the README to explain how to get past SmartScreen and Smart App Control, so that I can install it with confidence.

### Maintainer

137. As the maintainer, I want pushing a `v*` tag to build both architectures, compile both installers and create a draft Release with notes, so that releasing is one tag and one click.
138. As the maintainer, I want the tag to be the only source of the SemVer version, so that versions never disagree.
139. As the maintainer, I want publishing the Release to open the winget manifest PR, so that winget stays current.
140. As the maintainer, I want all behaviour testable through one core library with fakes for HTTP, clock, network, browser and the data folder, so that I can change the app without fear.
141. As a contributor, I want the look to live in copyable ResourceDictionaries checked by side-by-side screenshots, so that I can keep the app matching the prototype.

## Implementation Decisions

### Stack and process (ADR 0001)

- WinUI 3 + C# NativeAOT on .NET 10 (LTS) and the latest stable Windows App SDK 2.x. One process. Windows 11 only (min build 22000). x64 and ARM64; ARM64 is marked untested by the maintainer.
- Unpackaged and self-contained (Windows App SDK runtime bundled). A PerMonitorV2 app manifest is required. Publishing must also copy the `.pri` and `.xbf` files that `dotnet publish -o` leaves out.
- XAML with `x:Bind` only (no runtime `{Binding}`), CommunityToolkit.Mvvm view models, a hand-written composition root, no DI container, nothing that needs reflection. JSON with System.Text.Json source generation.
- Motion uses built-in XAML and Composition animations only. No Lottie, Win2D or shaders.
- Win32 declarations come from CsWin32: the low-level keyboard hook, z-order pinning, the Dock window, monitor and DPI calls, DPAPI.
- Single instance via a named mutex. A second launch signals the running instance to raise the Widget, then exits.
- **Budget** (signed off; release build, after 5 min, summed over all processes, judged on private working set and commit): docked ≤100 MB commit (hard cap 150); expanded with 50 Tasks ≤150 MB commit; idle CPU ≤0.1% over 5 min; hotkey to focused Capture box ≤100 ms p95; animations at 60 fps; installer ≤20 MB. The spike measured 55 / 75 MB docked and 78 / 99 MB expanded.

### Modules

The app splits into a **core** library holding all behaviour and a **shell** holding WinUI and Win32. The shell has no logic of its own.

**Core (one public seam).** A plain .NET library, no WinUI references. Its public face is an app model that the shell's views bind to and send commands to:

- Commands: commit Capture, update draft, clear draft, tick / untick, delete, edit Title / Details, cycle Priority / Effort, move (to index), Re-sort, undo, redo, Re-interpret (with corrected text), Retry / Edit / Make a Task as-is / Discard on a Capture row, sign in, cancel sign-in, sign out, choose model, change settings, check for updates, start update, dismiss banner.
- Observable state: the ranked list of rows (Task rows, pending, waiting and failed Capture rows), the Done view, the open Task count and per-Priority counts, whether manual positions exist, the current banner, the attention dot, the sign-in state, whether anything is processing, read-only mode, the update state, and Settings.
- Outside dependencies, passed in by the composition root: an HTTP message handler (for OpenAI and GitHub), a clock, a network-availability source, a browser launcher, the data folder path, and the DPAPI protect/unprotect pair.

Inside the core, these deep modules sit behind that model:

- **Task list and ranking.** Holds open, Done and Deleted Tasks and manual positions. Ranking: Priority (High, Medium, Low), then Effort (Quick, Short, Long), then oldest first. Tasks from one Capture keep spoken order as a tie-break. Insertion rule: a new, edited or un-completed Task goes just before the first Task, scanning from the top, that the rule ranks strictly below it. With no manual moves this equals pure rule order. A drag changes only position. Re-sort discards all manual positions. No ageing and no deadline escalation.
- **Undo history.** In memory only, from when the Widget opens. Covers complete, delete, edit, move, Re-sort, Re-interpret and clearing the draft with ×.
- **Capture pipeline.** Owns Capture records and their states (pending, waiting with a cause, failed with a cause, interpreted). Saves the Capture before sending its request. Runs Captures concurrently. Holds waiting Captures while offline, signed out or never connected, and releases them when that clears. Re-interpret keeps the old not-Done Tasks until the new ones arrive, then swaps them in one undoable step.
- **ChatGPT client.** Sign in with ChatGPT and the Responses API, written by hand over HTTP. OpenAI's devkit has a noncommercial licence, so don't use it.
- **Store.** `tasks.json`, `settings.json`, `archive.jsonl` and the token file.
- **Update checker.** GitHub Releases query, version compare, download, launch the installer.

**Shell.** Views copied from the prototype, plus the Win32 pieces: the tap hotkey hook, Widget z-order pinning, the Dock window, home-monitor placement, full-screen detection, backdrops, theme listening, focus defence, autostart value, and the loopback HTTP listener for sign-in.

### Capture flow

- The Capture box sits at the top of the expanded Widget and is always present. Its placeholder is "Capture a thought…" (prototype). Its tooltip reads "Type or dictate. Enter adds it, Shift+Enter is a new line." That is the only in-app "Type or dictate" copy. Wispr Flow is named only in the README ("works well with dictation tools like Wispr Flow").
- **Draft ×.** A bare icon button (the prototype's `Bare` style and its × glyph) at the right end of the Capture box, shown only while the box has text. It clears the box and the saved draft, keeps focus in the box, and is undoable with Ctrl+Z.
- **Tap Ctrl+Shift.** Detected with a `WH_KEYBOARD_LL` hook. It fires on release, only if both keys were pressed and released with no other key in between, within 1 s. Firing on press would hijack Ctrl+Shift+X shortcuts. Rebindable in Settings to another modifier tap or a chord (a chord uses `RegisterHotKey`). Don't default to Ctrl+Alt+Space (taken on the maintainer's machine) or Wispr Flow's defaults (Ctrl+Win, Ctrl+Win+Space, Ctrl+Win+Alt, Shift+Alt+Z/X).
- Docked → tap: expand, raise, focus the Capture box on the home monitor.
- Open → tap: if the box has text, commit and dock. If empty, show "Waiting for dictation…" with the thin indeterminate bar for up to `DictationWaitMs` (4000). Commit and dock when text arrives (Flow pastes each dictation in one go). Dock with no Capture if nothing arrives.
- Enter commits and stays open with an empty box. Shift+Enter is a newline. Esc or clicking another window docks without committing. The text stays as the draft.
- Read the text from the field, never from the clipboard.
- **Focus defence.** A deactivation does not dock the Widget when the window taking focus belongs to Wispr Flow's process.
- The draft is persisted with the debounced save, so it survives a restart.

### Capture contract (v4, binding)

- Copy contract v4 (linked at the top) word for word: schema, instructions text, today's-date format, tolerant parse.
- Schema, from the prototype:

  ```
  { tasks: [ { title: string, details: string, priority: "high"|"medium"|"low", effort: "quick"|"short"|"long" } ] }
  ```

  Strict: every field required, no extra properties. No actionable Task is `tasks: []`. No Details is `""`. Array order is spoken order.
- Request: `instructions` is the v4 text with today's date filled in ("Today is Wednesday 7 Oct 2026"). `input` is one user message holding only the Capture text. Re-interpret sends only the corrected Capture text, in the same call shape. Nothing else about the user is sent.
- Make a Task as-is (as in the prototype's model): the first line of the Capture text becomes the Title, with Medium Priority, Short Effort and empty Details. The Task keeps its Capture, is placed by the insertion rule, and the step is undoable.

### ChatGPT client (ADR 0002, ADR 0003)

- **Connection.** ChatGPT through Sign in with ChatGPT is the only Connection. There is no API key and no fallback provider.
- **Sign-in.** System browser to the authorize endpoint with `client_id=dynamic_agent_client`, a `http://127.0.0.1:<port>/auth/callback` loopback, PKCE S256, `state` and an OIDC nonce. Scopes `openid profile email offline_access resource.invoke chatgpt.tokens.use.direct`, `resource=https://api.openai.com/v1`. Save the issued client ID and a per-install opaque `ext_agent_host_id`. Check that the granted scopes include `chatgpt.tokens.use.direct` before any inference call. The callback serves a tiny page: "Signed in, you can close this tab". Then the Widget comes to the front.
- Sign-in times out after 5 minutes. Cancel, timeout, denial, bad `state` or a failed token exchange give "Sign-in didn't finish" with Try again, and the cause in a tooltip. No banner, no dialog.
- **Plan check.** After the token exchange, call `GET /v1/models`. On 403 `subscription_sharing_user_not_eligible`, or an empty list, discard the tokens and stay signed out with the plan message and the "See ChatGPT plans" link. Free plans aren't blocked up front.
- **Tokens.** The access token lasts 1 h. The refresh token lasts 30 days and rotates. Refresh proactively when under 5 minutes remain, and serialize all refreshes. On a 401, do one serialized refresh, then retry. If the refresh fails, the user is signed out. Re-auth after a failed refresh sends `login_hint` for the same account. A user-initiated sign-in sends no hint, so the account picker shows.
- **Sign out.** No confirmation. Revoke at `/oauth/revoke`, delete the token file, keep Tasks and Captures.
- **Token storage.** One per-user DPAPI-encrypted file in `%LOCALAPPDATA%\TaskWidget\` holding the issued client ID, `ext_agent_host_id`, and the access, refresh and ID tokens with expiry. Not Credential Manager, because the record (~3.4 KB) is over its 2,560-byte limit.
- **Model.** Read the response's `models` key (not `data`). Cache the list on sign-in and refresh it at most daily. Automatic picks the first listed of `gpt-5.6-sol`, `gpt-reserve`, `gpt-5.6-terra`, `gpt-6-astra`. If none is listed, take the listed model with the best `priority`. A user choice from the Settings dropdown falls back to Automatic silently if it disappears. Always send `reasoning: {effort: "low"}`.
- **Request.** `POST /v1/responses` with `stream: true`, `store: false`, `instructions`, and `input` as a message list (`[{role:"user", content:[{type:"input_text", text}]}]`; a plain string returns 400). `text.format` is `{type:"json_schema", strict:true}` with the v4 schema. Don't send `temperature`, `max_output_tokens` or `role: "system"` items; they're rejected.
- **Timeouts and retries.** 30 s per attempt. Network error, 5xx or `response.failed`: retry once after ~2 s. 429 (including `subscription_sharing_usage_limit_exceeded`): never retried automatically. Bad output (invalid JSON or schema mismatch after the tolerant parse): retry once. In-flight requests can be cancelled.

### Failure causes

After the automatic retries above, a Capture ends in one of these states:

| Cause | Row | Reason shown | Primary action | Dot |
|---|---|---|---|---|
| Offline | Waiting | "Waiting for connection" | runs on its own when the network returns | No |
| No Connection yet | Waiting | "Connect your ChatGPT account" | runs once signed in | Yes |
| Signed out (refresh failed or signed out) | Waiting | "Signed out" | Sign in (banner); all waiting Captures then run | Yes |
| Timeout / 5xx | Failed | "Couldn't reach ChatGPT" | Retry | Yes |
| 429 | Failed | "Rate-limited by ChatGPT" | Retry | Yes |
| Bad output | Failed | "Couldn't understand the reply" | Retry | Yes |
| Zero Tasks | Failed | "No task found in this" | Make a Task as-is | Yes |
| Plan lapsed (403 not eligible on a Capture) | Failed | "Your ChatGPT plan can't be used here" | Retry | Yes |
| Re-interpret failed | Failed | "Re-interpret failed" (with corrected text) | Retry; Discard keeps the old Tasks | Yes |

- Failed rows offer Retry · Edit · Make a Task as-is · Discard. They persist across restarts. Tooltip detail copy is in the prototype's `Model.cs`.
- Signed out is the only Connection problem that shows the header banner and holds new Captures.
- The attention dot is lit while any of these hold: a failed row exists, signed out, no Connection yet, an update is available. New Tasks never light it. It clears when its cause is dealt with.

### Widget and Dock

- Look and motion: the prototype, exactly. Widget "Fluent list" (`WidgetA.xaml`), Dock Capture bar (`DockCapture.xaml`), Settings (`SettingsPanel.xaml`), Undo pill, round-4 screens (`Round4/*.xaml`), icons (`Icons.cs`), and the enter / leave / dim animations (`Fx` in `Helpers.cs`).
- Header: open Task count, Re-sort icon (only while manual positions exist), Done view toggle, Settings gear, collapse (⌄).
- Banners, one at a time, by precedence: newer version > signed out > recovered from backup / started empty. Tint per the prototype's status brushes.
- Keyboard: ↑/↓ select, Space tick, Enter expand, E edit Title, Del delete, Ctrl+↑/↓ move, Ctrl+Z / Ctrl+Y undo and redo. Mouse equals keyboard. Drag needs press-and-hold or a movement threshold, so a click still expands.
- Strike hold 1500 ms; Undo pill 5000 ms; other timings and easings from `Motion.xaml`.
- Welcome (first launch until first sign-in): plain lines under the Capture box, above any waiting Captures. After the first sign-in, an empty list shows only the hotkey line.
- The Widget opens expanded on first launch. Afterwards it opens in the last state (Widget or Dock), stored in `settings.json`.
- The Dock: clicking anywhere on it opens the Widget with the Capture box focused. Per-Priority counts of open Tasks at the right (zero levels hidden). Spinner and "Interpreting…" while any Capture processes. Accent attention dot top-right.
- WinUI constraints from the prototype: windows are rounded rectangles with DWM's 8 px corners (a live backdrop can't be per-pixel transparent). Widget ↔ Dock animates content and window height, since the backdrop can't fade. Listen to `UISettings.ColorValuesChanged`, not `AccessibilitySettings.HighContrastChanged` (it throws when unpackaged). Size the outer frame from the measured frame delta, not `AppWindow.ResizeClient`. `x:Bind` evaluates identical function calls once and can't nest them under OneWay.

### Desktop placement

- **Widget.** A normal top-level window pinned to the bottom of the z-order (`SetWindowPos(HWND_BOTTOM)`, enforced in `WM_WINDOWPOSCHANGING`). The hotkey raises it to the front, and it re-pins on deactivate. No Progman/WorkerW reparenting (broken on 24H2, dies on Explorer restart). Stays visible on Win+D.
- **Dock.** A borderless `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE` edge window, topmost while shown, not an AppBar (reserves no space). Recreate or re-anchor on Explorer's `TaskbarCreated`.
- **Home monitor.** The Widget sits at the top-right of the work area with a 28 px margin. The Dock is centred 8 px under the top of the work area (under a top taskbar, or under the screen edge with an auto-hiding one). First run uses the primary monitor. Dragging the Widget by its header strip to another monitor makes that monitor home, and the Widget snaps to its anchor there with the prototype's easing (dropped on the same monitor, it snaps back). The home monitor is stored by device identity. No pixel positions are stored, and there's no Settings picker.
- While the home monitor is disconnected, both windows use the primary. They return when it reconnects. Only a drag changes the saved home.
- Re-anchor on display-change, DPI-change and work-area-change. The Widget's height is capped at the work-area height and scrolls beyond it.
- **Full screen.** The Dock hides while a full-screen app is on the home monitor (`ABN_FULLSCREENAPP`-style detection) and returns afterwards. The hotkey still raises the Widget over it, and it goes away on commit or Esc. The Dock stays hidden throughout.
- **Virtual desktops.** Show on every desktop if a public window style achieves it. Otherwise stay on the starting desktop (accepted for v1). Confirm at build time. No undocumented virtual-desktop COM.

### Theming

- Follow Windows dark/light (`UISettings`) with a Theme override (System / Light / Dark). Accent only on interactive bits. Fixed per-Priority colours per theme from `Tokens.xaml`.
- Backdrop: Mica, Mica Alt, Desktop Acrylic or Solid via `MicaController` / `DesktopAcrylicController` with `IsInputActive = true`, so it stays live when inactive. Only Mica was verified kept-active in the spike; confirm the other two. When Windows makes the chosen backdrop unavailable (transparency off, battery saver, Remote Desktop, contrast theme), fall back to Solid, show the Settings note, return when it's available, and never overwrite the setting.
- Contrast themes: the `HighContrast` token dictionary. System colours, no backdrop, Priority by badge shape, hairlines in the text colour.
- No palette computed from the wallpaper.

### Storage (ADR 0004)

- Folder `%LOCALAPPDATA%\TaskWidget\`: `tasks.json`, `settings.json`, `archive.jsonl`, the DPAPI token file. No portable mode.
- `tasks.json` holds Tasks (open, Done, Deleted, with manual positions), Capture records (original text, the latest interpreted text, state and cause, the Tasks each produced), the draft and `schemaVersion`. Loaded whole into memory.
- `settings.json` holds Settings, Widget/Dock state, the saved home monitor identity, whether the welcome has been retired, the cached model list, and update-check state. A corrupt `settings.json` silently falls back to defaults.
- Writes: debounced ~300 ms after any change, flushed on exit and on Windows shutdown. A new Capture is written before its request is sent. Each save writes `tasks.json.tmp`, then `File.Replace` swaps it in, keeping `tasks.json.bak`.
- Load: if `tasks.json` is unreadable, rename it `tasks.corrupt-<timestamp>.json` (never delete it) and load `.bak` (recovered banner, with the backup's time). If `.bak` is bad too, start empty (started-empty banner). Each banner shows once per event.
- `schemaVersion`: an older file is migrated forward after a `tasks.v<N>.bak` copy. A newer file opens the Widget read-only with its banner. Ticking, editing and committing are disabled, while typing a draft still works.
- Retention: Done Tasks older than 90 days, and Captures whose Tasks have all gone, move to the append-only `archive.jsonl`, which the app never reads. Deleted Tasks are purged 30 days after deletion. The Ctrl+Z history is never stored.
- No export in v1. The README documents the data path.

### Packaging, updates, release (ADR 0005)

- Inno Setup 7, one installer per architecture: `TaskWidget-<version>-x64.exe`, `TaskWidget-<version>-arm64.exe`. Per-user (`PrivilegesRequired=lowest`), `MinVersion=10.0.22000` with a "requires Windows 11" message. No portable zip.
- The installer's last page has "Start Task Widget when I sign in" (checked), which writes the HKCU `Run` value. The Settings toggle changes it later. The app never rewrites the value on launch. The uninstaller always removes it.
- Uninstall asks "Also delete your tasks and settings?", keeping them by default. A silent uninstall keeps them.
- Update check: once a day at start-up, if Check for updates automatically is on (default on), query GitHub Releases for the latest published release (ignore drafts and pre-releases). Settings › About states, from the prototype: up to date, checking, "Version x.y.z is available" with What's new and Update, downloading ("Task Widget restarts to finish"), couldn't check. Check now runs it by hand. An available update lights the dot. Update downloads the installer, runs it silently and restarts the app. No updater process runs between checks, and nothing about the user is sent.
- Signing: v1 ships unsigned and the README explains SmartScreen and Smart App Control. After v1 ships, apply to SignPath Foundation and sign from v1.x (Certum OSS certificate is the fallback).
- Distribution: GitHub Releases plus winget. No Scoop.
- Release pipeline: a GitHub Actions workflow on `v*` tags builds both NativeAOT apps, compiles both installers, signs once SignPath is set up, and creates a draft Release with notes. The maintainer publishes by hand. Publishing triggers the winget manifest PR. SemVer; the tag is the only source of the version.

### README

- Privacy section: only the Capture text and today's date are sent to ChatGPT, with `store: false`. Usage counts against the user's ChatGPT plan (Go, Plus or Pro works; Free unverified). The only other network call is the daily GitHub Releases update check, which can be turned off. Tokens are stored DPAPI-encrypted on this PC.
- Data path, SmartScreen / Smart App Control instructions, the Wispr Flow line, and the note that Ctrl+Shift is the input-language toggle for users with two keyboard languages.

## Testing Decisions

- **One seam: the core's app model.** Every behaviour test goes through the core's public commands and observable state, and through the files it leaves in the data folder. Tests assert what a user would see: which rows exist in what order, their states and reasons, the banner, the dot, the counts, read-only mode. They never reach into ranking internals, the pipeline's queues or the store's classes.
- **Fakes stand in only for the outside world:** a fake `HttpMessageHandler` serving the OpenAI endpoints (`/oauth/token`, `/oauth/revoke`, `/v1/models`, streamed `/v1/responses`) and GitHub Releases; a controllable clock; a network-availability switch; a browser launcher that hands back a loopback callback (or doesn't); a temp data folder; and a pass-through DPAPI pair. None of our own code is mocked.
- **Framework:** xUnit v3, running under the normal JIT in CI. The shipped app is NativeAOT; a CI step also publishes the AOT build so trim and AOT warnings fail the build.
- **What a good test looks like:** set up the world (fake replies, files on disk, clock), drive commands as the user would, and check the visible outcome. Example: commit "email Sarah the invoice and buy milk" with the fake returning two Tasks, expect a pending row and then two ranked rows. Example: start with a corrupt `tasks.json` and a good `.bak`, expect the recovered banner, the backup's Tasks and a `tasks.corrupt-*.json` file.
- **Areas to cover through the seam:** ranking and the insertion rule with manual moves and Re-sort; undo / redo of every action; tick strike-hold cancel (with the fake clock); every row in the failure table, including retries and the 30 s timeout; waiting Captures released on reconnect and on sign-in; Re-interpret success and failure; Make a Task as-is; the draft surviving a restart; the request shape (message-list input, `store: false`, `stream: true`, reasoning low, strict schema, today's date in the instructions, only the Capture text sent); serialized and proactive refresh; plan check at sign-in; sign-out keeping data; model preference list and silent fallback; tolerant parse; storage durability (atomic save, `.bak` recovery, started empty, newer `schemaVersion` read-only, older migrated, Capture saved before its request); retention moves to `archive.jsonl`; update-check states and drafts / pre-releases ignored.
- **Contract quality** isn't unit-tested. The v4 contract was tuned on live ChatGPT; its harness on `prototype/capture-contract` stays the way to re-check it if the contract or default model changes.
- **The shell is checked by hand, not unit-tested:** hotkey tap, z-order and Win+D, Dock window, home monitor and DPI, full-screen hiding, backdrops and themes, Wispr Flow focus. Fidelity to the prototype is checked with side-by-side screenshots of every prototype scene, in light, dark and contrast themes. The prototype's `LOOK_*` start-up overrides reach each scene.
- **Budget check before each release:** re-run the spike's measurement harness ([`prototypes/stack-spike`](https://github.com/Gul-Sher-Khan/task-widget/tree/prototype/stack-spike/prototypes/stack-spike)) against the release build. It must meet the Budget above.
- **Prior art:** none in the repo yet; this spec sets the pattern. The prototype's fake interpreter and simulated sign-in outcomes show which states the fakes must be able to produce.

## Out of Scope

- An API-key Connection (Groq, OpenRouter, custom OpenAI-compatible) or any fallback provider (ADR 0003).
- Local or on-device models.
- A palette computed from the wallpaper; custom theme packs beyond dark/light and the accent.
- Reminders, due dates, notifications.
- Sync across devices; mobile.
- The Windows 11 Widgets board (Win+W). The Widget is our own window.
- Search in the Done view; showing or restoring Done Tasks older than 90 days.
- Export or backup features.
- A portable zip, Scoop, MSIX, Windows 10.
- Delta updates, silent auto-update.
- A first-run wizard, tour or coach marks.
- Persisting the undo history across sessions.
- Undocumented virtual-desktop APIs and WorkerW desktop pinning.

## Further Notes

- **Unverified, confirm while building:** whether Mica Alt and Acrylic stay live with `IsInputActive = true` (only Mica was measured); whether a public window style shows the windows on all virtual desktops; whether the Wispr Flow Bar ever takes focus (the focus rule is cheap either way); whether the global keyboard hook trips antivirus during winget validation; the installer's real size per architecture (estimated 16–18 MB); Free-plan eligibility and per-request quota cost.
- **Build gotchas from the spike:** the NativeAOT link step needs `vswhere` on PATH; `dotnet publish -o` drops `.pri` / `.xbf`; without the PerMonitorV2 manifest Windows bitmap-stretches the window.
- **Sign in with ChatGPT terms** (2026-09-29): use our own app name, requests come from the user's own runtime, no general API proxying, users are never charged, no token pooling, a privacy notice is required, and follow OpenAI's [UI/UX guidelines](https://developers.openai.com/siwc/ui-ux-guidelines) for the sign-in button.
- **After v1:** send the SignPath Foundation application.
- Research write-ups behind these decisions live on the `research/*` branches, linked from the map's closed tickets.
