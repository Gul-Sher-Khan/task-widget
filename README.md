# Task Widget

Task Widget is a small Windows 11 app. The Widget is a window pinned to the desktop that lists your Tasks, ranked so important, quick Tasks come first. The Dock is the collapsed Widget under the top edge of the screen.

Tap Ctrl+Shift anywhere, type or dictate into the Capture box, and tap Ctrl+Shift again. The Widget docks. The Connection then turns the Capture into one or more Tasks. Sign in with ChatGPT is how you create the Connection, and there is no other way.

The app works well with dictation tools like Wispr Flow.

Ctrl+Shift is the Windows input-language toggle for people with two keyboard languages, so they may want to rebind the hotkey in Settings.

Task Widget is free and open source under the [MIT License](LICENSE).

## Download and run

You need Windows 11 and a ChatGPT account (Go, Plus or Pro).

1. Open the [latest release](https://github.com/Gul-Sher-Khan/task-widget/releases/latest).
2. Under Assets, download the installer for your PC:
   - Most PCs: `TaskWidget-<version>-x64.exe`
   - ARM64 PCs (for example Snapdragon laptops): `TaskWidget-<version>-arm64.exe`. The maintainer has not tested the ARM64 build.

   Not sure which you have? Open Settings › System › About and look at System type.
3. Run the installer. It installs for your user only and does not need an administrator. If Windows warns you, see [If Windows blocks the installer](#if-windows-blocks-the-installer).
4. Leave "Start Task Widget when I sign in" checked on the last page if you want it at every sign-in, then click Finish. Task Widget starts.

You can start it again later from the Start menu: search for Task Widget.

### First use

The Widget opens at the top right of your screen with a short welcome.

1. Click **Sign in with ChatGPT**. Your browser opens. Sign in and allow access. The tab then says you can close it, and the Widget comes back to the front.
2. Tap Ctrl+Shift (press both keys and let go), say or type a thought, and tap Ctrl+Shift again. A few seconds later it becomes one or more Tasks.
3. Click ⌄ in the header to collapse the Widget into the Dock. Click the Dock to open it again.

Enter also adds a Capture and keeps the Widget open. Shift+Enter starts a new line. Click a Task to see its Details. Tick it when it's done. Right-click a Task to edit, re-interpret or delete it. Ctrl+Z undoes.

You can capture before you sign in. Those Captures wait and run once you sign in.

### winget

A winget package is planned. Until it's listed, install from GitHub Releases as above.

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

## Updates

Task Widget checks GitHub Releases once a day. When a new version is out, the Dock's dot lights and Settings › About shows it. Click Update there: the app downloads the new installer, runs it, and restarts with your Tasks and draft kept.

## Uninstall

Open Settings › Apps › Installed apps, find Task Widget, and choose Uninstall.

## If Windows blocks the installer

v1 ships unsigned.

**SmartScreen.** Running the installer can open a window titled "Windows protected your PC". The text says Microsoft Defender SmartScreen prevented an unrecognized app from starting, and the publisher is unknown. Click More info, then Run anyway.

If the browser warns that the download is uncommon, keep the file and then run it. In Edge, choose Keep on that warning.

**Smart App Control.** If Windows says Smart App Control blocked the app, it will not allow this installer on its own. v1 has no signature. While Smart App Control is on, it blocks the installer as untrusted.

Open the Start menu, type Windows Security, and open it. Select App & browser control, then Smart App Control settings, then Off. Run the installer again.

If that page says Evaluation, Smart App Control is not blocking the installer. Leave it set to Evaluation.

You can turn Smart App Control back on later from the same page.

## Build from source

You need:

- Windows 11
- The .NET 10 SDK (see `global.json`)
- Visual Studio 2022 or later, or its Build Tools, with the **Desktop development with C++** workload. NativeAOT needs its linker. For an ARM64 build, also add the MSVC ARM64 build tools.

Build and test:

```
dotnet build TaskWidget.slnx -c Release
```

```
dotnet test TaskWidget.slnx -c Release --no-build
```

Run a debug build:

```
dotnet build src/TaskWidget.Shell -c Debug -p:Platform=x64
```

The app is then at `src/TaskWidget.Shell/bin/x64/Debug/net10.0-windows10.0.26100.0/win-x64/TaskWidget.exe`. Run as is, it uses your real data folder. Set `LOOK_SCENE=0` (scenes 0 to 7) before starting it to open sample data in a temporary folder instead. `LOOK_THEME`, `LOOK_BACKDROP`, `LOOK_DOCKED` and `LOOK_SETTINGS` pick other states. They match the look prototype's overrides.

Publish the app as it ships (NativeAOT, self-contained):

```
dotnet publish src/TaskWidget.Shell/TaskWidget.Shell.csproj -c Release -r win-x64 -o artifacts/win-x64
```

If the publish can't find the linker, put `%ProgramFiles(x86)%\Microsoft Visual Studio\Installer` (where `vswhere.exe` lives) on PATH.

The installer script is `installer/TaskWidget.iss`, compiled with Inno Setup 7 through `installer/compile.ps1`.

## Releasing

Releases come from git tags. The tag is the only source of the version.

1. Push a SemVer tag, for example `git tag v1.2.0` and `git push origin v1.2.0`.
2. The Release workflow builds both architectures, compiles both installers and creates a draft release with notes.
3. Check the draft on GitHub, then publish it. Users' apps see it at their next daily check.

Publishing also runs the winget workflow, which opens the winget-pkgs pull request once a `WINGET_TOKEN` secret (a classic personal access token with the `public_repo` scope) is set. Signing with SignPath turns on once its secret and variables are set; until then releases ship unsigned.

## Contributing

Issues and pull requests are welcome. Read `AGENTS.md` first. The look prototype on the `prototype/widget-look` branch is binding, so a change to how the Widget looks or moves starts there. `SPEC.md` and the ADRs in `docs/adr/` record the decisions, and `GLOSSARY.md` defines the terms.

Behaviour lives in `src/TaskWidget.Core` and is tested through its app model in `tests/TaskWidget.Tests`. `src/TaskWidget.Shell` holds the WinUI views and Win32 pieces.

## License

MIT. See [LICENSE](LICENSE). Third-party components are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
