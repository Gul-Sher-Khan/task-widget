# Task Widget

Task Widget is a small Windows 11 app. The Widget is a window pinned to the desktop that lists your Tasks, ranked so important, quick Tasks come first. The Dock is the collapsed Widget under the top edge of the screen.

Tap Ctrl+Shift anywhere, type or dictate into the Capture box, and tap Ctrl+Shift again. The Widget docks. The Connection then turns the Capture into one or more Tasks. Sign in with ChatGPT is how you create the Connection, and there is no other way.

The app works well with dictation tools like Wispr Flow.

Ctrl+Shift is the Windows input-language toggle for people with two keyboard languages, so they may want to rebind the hotkey in Settings.

## Privacy

Only the Capture text and today's date are sent to ChatGPT, with `store: false`.

Usage counts against your own ChatGPT plan. Go, Plus or Pro works. Free is unverified.

The only other network call is the daily GitHub Releases update check. Turn it off with Check for updates automatically in Settings.

Tokens are stored DPAPI-encrypted on this PC.

Nothing else leaves this PC. There is no API key. The app does not charge you.

## Your files

The app keeps your files in `%LOCALAPPDATA%\TaskWidget\`.

Back up that folder. It holds `tasks.json`, `settings.json`, `archive.jsonl` and the DPAPI-encrypted token file.

`tasks.json` holds your Tasks, Captures and draft. `settings.json` holds your settings. `archive.jsonl` holds Done Tasks older than 90 days, and Captures whose Tasks are all gone. The Widget does not load that file. The token file holds the ChatGPT sign-in tokens.

A silent uninstall does not delete them. Keeping them is the default. The uninstaller asks "Also delete your tasks and settings?" and leaves the folder unless you choose to delete it.

## Install

Windows 11 only. The installer refuses Windows 10.

Download a per-user installer from [GitHub Releases](https://github.com/Gul-Sher-Khan/task-widget/releases). It does not need an administrator. On an x64 PC, use `TaskWidget-<version>-x64.exe`. On an ARM64 PC, use `TaskWidget-<version>-arm64.exe`. The maintainer has not tested the ARM64 build.

You can also install with winget. Search for Task Widget, then install the package id that search prints.

```
winget search "Task Widget"
```

## If Windows blocks the installer

v1 ships unsigned.

**SmartScreen.** Running the installer can open a window titled "Windows protected your PC". The text says Microsoft Defender SmartScreen prevented an unrecognized app from starting, and the publisher is unknown. Click More info, then Run anyway.

If the browser warns that the download is uncommon, keep the file and then run it. In Edge, choose Keep on that warning.

**Smart App Control.** If Windows says Smart App Control blocked the app, it will not allow this installer on its own. v1 has no signature. While Smart App Control is on, it blocks the installer as untrusted.

Open the Start menu, type Windows Security, and open it. Select App & browser control, then Smart App Control settings, then Off. Run the installer again.

If that page says Evaluation, Smart App Control is not blocking the installer. Leave it set to Evaluation.

You can turn Smart App Control back on later from the same page.
