# PROTOTYPE: stack memory and animation spike

Throwaway code for [Stack memory and animation spike](https://github.com/Gul-Sher-Khan/task-widget/issues/10). Not production code. Never merge into `main`.

- `winui/`: WinUI 3 + C# NativeAOT spike (Windows App SDK 2.5, .NET 8). Tap Ctrl+Shift or press Ctrl+Alt+T to open and close; F2 cycles backdrops.
- `tauri/`: Tauri 2.12 spike with the same behaviour (Ctrl+Alt+T only).
- `measure.ps1`: harness that measures memory and CPU across the whole process tree, plus hotkey → focused latency. `run-all.ps1` runs every measurement.
- `probe-hotkeys.ps1`: lists which global hotkey combos are free.
- `results/`: raw JSON from the runs reported on the issue.

Build notes: the NativeAOT publish needs the VS Build Tools C++ workload, with the VS Installer folder on PATH (for `vswhere`). After publishing, copy `SpikeWinUI.pri`, `App.xbf` and `MainWindow.xbf` from `bin/` into the publish folder; `-o` publishing drops them.
