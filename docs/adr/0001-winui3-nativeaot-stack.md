# WinUI 3 + C# NativeAOT, unpackaged, Windows 11 only

Task Widget runs next to about ten heavy apps on a machine at ~80% memory, so the stack was picked on memory first. WinUI 3 with C# NativeAOT met the agreed budget on the user's machine; Tauri 2 did not, because WebView2 costs about 150–250 MB on its own. We build on **.NET 10 (LTS) and the latest stable Windows App SDK 2.x**, compiled with NativeAOT, as a single process.

## Decisions

- **Platform:** Windows 11 only (min `10.0.22000`). The installer refuses Windows 10 with a clear "requires Windows 11" message.
- **Architectures:** x64 and ARM64 builds from CI. ARM64 is marked untested by the maintainer.
- **App model:** unpackaged, with the Windows App SDK runtime bundled (self-contained). Needs a PerMonitorV2 `app.manifest`, or Windows stretches the window and it looks blurry. Publishing must copy the `.pri`/`.xbf` files, which `dotnet publish -o` leaves out. MSIX was rejected because sideloading it needs a trusted signing certificate, which is a burden for an open-source download.
- **UI code:** XAML with `x:Bind` only (no runtime `{Binding}`), CommunityToolkit.Mvvm for view models, and a hand-written composition root with no DI container. Everything that needs reflection is avoided, so the AOT build stays trimmed.
- **Rendering and motion:** built-in XAML and Composition animations only. No Lottie, Win2D or custom shaders unless a measured RAM cost justifies one.
- **Backdrop:** `MicaController` (Mica, Mica Alt) or `DesktopAcrylicController`, with `IsInputActive = true` so the chosen backdrop stays live when the window is inactive. When Windows makes the chosen backdrop unavailable (transparency effects off, battery saver, Remote Desktop, contrast theme), the Widget and Dock fall back to Solid under the same theme rules. They return to the user's choice when it becomes available again, and the user's setting is never overwritten.
- **Native Windows calls:** CsWin32 generates the declarations for the low-level keyboard hook, z-order pinning, the Dock window and DPAPI.
- **Prototype carry-over:** the look prototype is a real WinUI 3 project with fake Tasks, and its whole look lives in XAML ResourceDictionaries and styles (colours, spacing, type, animation timings). The app copies those files unedited and replaces only the fake data. Fidelity is checked with side-by-side screenshots.

## Considered options

Measured in the spike under the user's normal load, release builds, summed over all processes after 5 minutes docked. Private working set / commit, against a commit budget of ≤100 MB docked (hard cap 150) and ≤150 MB expanded:

| | WinUI 3 AOT | Tauri 2.12 |
|---|---|---|
| Processes | 1 | 7 |
| Docked | 55 / 75 MB | 114 / 180 MB |
| Expanded, 50 Tasks | 78 / 99 MB | 182 / 249 MB |
| Hotkey → focused, first press | 81 ms | 113 ms |

- **Tauri 2** would have shipped an HTML/CSS prototype unchanged, but it breaks the hard caps even when docked.
- **Electron** has the same WebView-class floor and a ~385 MB bundle.
- **Avalonia, Flutter, Slint, iced/egui** have weaker or unverified backdrop support, and their memory claims are unverified.

Don't reopen "just use web tech" without new memory measurements.
