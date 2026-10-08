# Task Widget

A personal desktop task inbox: spoken or typed thoughts become ranked, actionable tasks shown in an always-available desktop widget.

## Input

**Capture**:
The raw text the user dictates or types into the Capture box and commits, before any interpretation. One Capture may yield one or more Tasks. A Capture is never lost: if it cannot be turned into Tasks, it stays in the Widget until the user deals with it. A Capture keeps its original text even after the user corrects it to Re-interpret. Text not yet committed is a draft, not a Capture; a draft also survives a restart.
_Avoid_: Note, entry, input, prompt

**Capture box**:
The input field at the top of the Widget, focused by the user's global hotkey, where a Capture is entered.
_Avoid_: Quick-add, popup, prompt window

**Connection**:
The user's ChatGPT account, linked to Task Widget through Sign in with ChatGPT and used to turn Captures into Tasks. It is the only way Task Widget interprets Captures.
_Avoid_: Provider, account, AI service, backend

## Tasks

**Task**:
A single action the user would complete and tick off on its own, derived from a Capture, made of a Title, Details, Priority and Effort. A Capture yields one Task per such action; a Task is never broken down further into steps. Every Task remembers the Capture it came from.
_Avoid_: Todo, item, bullet

**Title**:
The short, one-line bullet that names a Task. Starts with a verb and keeps the Capture's own words where it can.
_Avoid_: Summary, heading

**Details**:
The expandable supporting context of a Task, hidden until the user opens it. Holds only context taken from the Capture (people, deadlines, the reason), never invented steps. May be empty.
_Avoid_: Description, notes, body

**Priority**:
How important a Task is, one of three levels: **High** (something bad happens or someone is waiting if it is not done soon), **Medium** (matters, but nothing breaks this week), **Low** (nice to do). First set from the Capture; the user's change always wins.
_Avoid_: Importance, urgency, score

**Done**:
A Task the user has completed. Done Tasks leave the ranked list but stay visible in the Widget's Done view for 90 days, from which they can be returned to the list. After that they are archived: kept, but no longer shown.
_Avoid_: Archived, finished, closed

**Deleted**:
A Task the user has discarded as wrong or no longer relevant. Unlike a Done Task, it is never shown again anywhere in the Widget.
_Avoid_: Removed, cancelled, trashed

**Re-interpret**:
Running a Capture again, optionally after correcting its text, to replace that Capture's Tasks that are not yet Done.
_Avoid_: Re-process, regenerate, retry

**Effort**:
How much time a Task is estimated to take, one of three levels: **Quick** (15 minutes or less), **Short** (up to an hour), **Long** (more than an hour). First set from the Capture; the user's change always wins.
_Avoid_: Size, duration, cost

## Surfaces

**Widget**:
The always-present desktop window that lists Tasks, ranked so important, quick Tasks come first: by Priority, then Effort, then oldest first. The user may drag a Task to any position; new or edited Tasks are placed by the ranking around those manual positions, and a Re-sort restores pure ranking.
_Avoid_: Panel, board, app window

**Dock**:
The Widget's collapsed form: a slim bar at the top edge of the screen that expands back into the full Widget. Shows no text, only icons with counts of open Tasks per Priority, plus a dot when something needs the user's attention.
_Avoid_: Bar, tray, mini-mode

**Home monitor**:
The one display the Widget and Dock live on; they never appear on different displays. At first it is the main display, and it changes only when the user drags the Widget to another display. While the home monitor is disconnected, the Widget and Dock stand in on the main display and return once it reconnects.
_Avoid_: Screen, display setting, current monitor
