# Windows desktop layer, Dock and theming: feasibility

Research for issue #4 (child of map #1). Researched 2026-10-06 against Microsoft Learn and the source of Rainmeter, Lively Wallpaper, Tauri (`tao`, `window-vibrancy`) and Electron docs. The stack is not chosen yet, so this note lists the Win32/WinRT APIs and says which stacks can reach them.

## Bottom line

| Question | Recommendation | Confidence |
| --- | --- | --- |
| Widget on the desktop, not over apps | Normal top-level window kept at the bottom of the z-order (`HWND_BOTTOM`, enforced in `WM_WINDOWPOSCHANGING`), with a hotkey that raises it to the front and lets it drop back on deactivate. Do **not** reparent into Progman/WorkerW. | High |
| Dock at the top edge | Borderless, non-activating `WS_EX_TOOLWINDOW` popup that is topmost while shown and does **not** reserve screen space. Avoid `SHAppBarMessage` registration (except possibly as a fullscreen-detection trick). | High |
| Mica / Acrylic | `DwmSetWindowAttribute(DWMWA_SYSTEMBACKDROP_TYPE)` on a top-level window, Windows 11 build 22621+. Both materials go solid when the window is inactive, which is the widget's normal state; plan the design around that or use the Windows App SDK controller path, which lets the app keep `IsInputActive = true`. | High on API; medium on how it looks inactive (needs prototype) |
| Dark/light, accent, wallpaper | `UISettings` (`GetColorValue`, `ColorValuesChanged`) for theme and accent; `IDesktopWallpaper` or `SystemParametersInfo(SPI_GETDESKWALLPAPER)` for the image, re-read on `WM_SETTINGCHANGE`. Wallpaper colour has to be computed by the app from the image. | High |

Everything above is plain Win32/COM/WinRT, callable from Rust (`windows` crate), C#/.NET (WinUI 3, Avalonia via P/Invoke), C++, Flutter (via a platform plugin / FFI) and Electron (via a native addon, or the built-ins noted below). Nothing requires WinUI 3 specifically.

## 1. Desktop-layer placement

### Option A: Progman/WorkerW reparenting (Lively, Wallpaper Engine)

How it works: send the undocumented message `0x052C` to the `Progman` window so Explorer creates a `WorkerW` behind the desktop icons, then `SetParent` your window into it. It is undocumented Explorer internals, and Windows 11 24H2 changed them:

- Before 24H2 the icons (`SHELLDLL_DefView`) lived in a top-level `WorkerW` and the wallpaper `WorkerW` was a separate top-level window. From 24H2 on, `SHELLDLL_DefView` and the wallpaper `WorkerW` are both **children of Progman**. Rainmeter documents this with Spy++ trees in its source and detects 24H2 (build 26100.2454) by checking whether `user32!GetCurrentMonitorTopologyId` exists. Source: [rainmeter `Library/System.cpp`](https://github.com/rainmeter/rainmeter/blob/master/Library/System.cpp).
- Lively's source quotes guidance from Microsoft: in the "raised desktop" state, Progman is now created with `WS_EX_NOREDIRECTIONBITMAP` and DefView is a `WS_EX_LAYERED` child. An app that forces that state "will now need to create its own WS_EX_LAYERED child HWND that is z-ordered under the DefView window but above the WorkerW window". Lively now sends `0x052C` with `wParam=0xD, lParam=0x1`, detects the new layout through `WS_EX_NOREDIRECTIONBITMAP` on Progman, makes its window a layered `WS_CHILD` of Progman, and re-checks the WorkerW z-order. Source: [lively `WinDesktopCore.cs`](https://github.com/rocksdanister/lively/blob/core-separation/src/Lively/Lively/Core/WinDesktopCore.cs); user reports of the 24H2 breakage: [lively discussion #2464](https://github.com/rocksdanister/lively/discussions/2464).

Why it is a poor fit for Task Widget:

- **Undocumented and already broke once** (24H2). A third layout change would break the widget again.
- **Cross-process parent/child**: making your window a child of Explorer's window attaches the two threads' input queues, and some messages are blocked across processes. Raymond Chen: [Is it legal to have a cross-process parent/child or owner/owned window relationship?](https://devblogs.microsoft.com/oldnewthing/20130412-00/?p=4683). This is a problem for a window that takes keyboard input (the Capture box). Wallpaper engines are mostly display-only.
- **Explorer restart** destroys Progman and WorkerW and the child window with them. Lively listens for `TaskbarCreated` and re-attaches the window.
- **Backdrops**: Mica/Acrylic via DWM is a top-level-window feature (the APIs take a top-level `HWND` and describe "window" backdrops). A child of Progman would almost certainly lose it. *Not verified; prototype if this option is ever revived.*
- It sits *under* the icons, which suits a wallpaper, not an interactive widget.

### Option B: normal window pinned to the bottom of the z-order (Rainmeter "On desktop", Tauri `always_on_bottom`)

- Rainmeter does **not** reparent its skins. It keeps top-level windows positioned relative to the desktop-icons host window, using `SetWindowPos(..., HWND_BOTTOM, ...)` and a hidden helper window. It polls every 250 ms (`TIMER_SHOWDESKTOP`) to detect Win+D "Show desktop" and moves its "on desktop" skins above the icons host while the desktop is shown. Source: [rainmeter `System.cpp`](https://github.com/rainmeter/rainmeter/blob/master/Library/System.cpp) (`PrepareHelperWindow`, `CheckDesktopState`, `ChangeZPosInOrder`).
- Tauri 2 / `tao` ships this as `set_always_on_bottom`: `SetWindowPos(HWND_BOTTOM)`, plus forcing `hwndInsertAfter = HWND_BOTTOM` in `WM_WINDOWPOSCHANGING` so the window cannot be raised by accident. Source: [tao `window_state.rs`](https://github.com/tauri-apps/tao/blob/dev/src/platform_impl/windows/window_state.rs), [tao `event_loop.rs`](https://github.com/tauri-apps/tao/blob/dev/src/platform_impl/windows/event_loop.rs).
- **Send to back / bring to front** maps directly onto this: a global hotkey (`RegisterHotKey`) clears the bottom pin, calls `SetForegroundWindow` to activate the window, and re-pins on `WM_ACTIVATE(WA_INACTIVE)`. All documented APIs.
- Pitfall: **Win+D / Show desktop** minimises or hides normal windows. Handle it the way Rainmeter does (detect it and re-show), or accept that the widget hides along with everything else. *How Win+D treats a `WS_EX_TOOLWINDOW` bottom window on 24H2+ needs a prototype check.*

### Option C: topmost toggling

`SetWindowPos(HWND_TOPMOST / HWND_NOTOPMOST)`. Fine for a temporary "pin on top" mode, but as the resting state it breaks the "not over other apps" requirement. Electron exposes it as `setAlwaysOnTop(flag, level)`, and its docs note that levels from `floating` to `status` sit below the taskbar on Windows ([Electron BrowserWindow](https://www.electronjs.org/docs/latest/api/browser-window)).

**Recommendation:** Option B as the resting state, a hotkey to bring the window to the front, and optional topmost while the user is typing a Capture.

## 2. Top-edge Dock

### AppBar (`SHAppBarMessage`)

Source: [Using Application Desktop Toolbars](https://learn.microsoft.com/en-us/windows/win32/shell/application-desktop-toolbars).

- `ABM_NEW` registers, `ABM_QUERYPOS` → `ABM_SETPOS` → `MoveWindow` positions it, and `ABM_REMOVE` must be sent before the window is destroyed. "The system prevents other applications from using the desktop area used by an appbar": maximised windows shrink to fit around it.
- The app must re-run QUERYPOS/SETPOS on `ABN_POSCHANGED`, send `ABM_ACTIVATE` on `WM_ACTIVATE` and `ABM_WINDOWPOSCHANGED` on `WM_WINDOWPOSCHANGED`.
- On the taskbar's edge, "the system ensures that the taskbar is always on the outermost edge".
- Autohide: only one autohide appbar per edge, first come first served (`ABM_SETAUTOHIDEBAR`). An autohide bar does not need `ABM_NEW`.
- `ABN_FULLSCREENAPP` tells the bar when a fullscreen app opens or the last one closes, and the bar "must drop to the bottom of the z-order".
- The sample code uses `SM_CXSCREEN`, i.e. the primary monitor only. A bar on another monitor has to compute that monitor's rect itself (`MonitorFromWindow` / `GetMonitorInfo`).
- If the process crashes without `ABM_REMOVE`, the reserved strip can stay reserved until Explorer notices or restarts. *Commonly reported, not in the docs; treat as a risk.*

### Borderless edge window (no reservation)

A `WS_POPUP` + `WS_EX_TOOLWINDOW` (no taskbar button, per [The Taskbar: Managing Taskbar Buttons](https://learn.microsoft.com/en-us/windows/win32/shell/taskbar#managing-taskbar-buttons)) + `WS_EX_NOACTIVATE` window placed at the top of the target monitor's `rcMonitor` from `GetMonitorInfo`, topmost while visible. It overlaps app content instead of pushing it down, and it has none of the AppBar bookkeeping. This matches the Game Bar feel: the Xbox Game Bar is an overlay summoned over the current app and does not resize other windows (observed behaviour, not a documented API).

**Recommendation:** borderless edge window. A slim Dock that steals a strip of every maximised window around the clock costs a lot for a task list. If the Dock should hide during fullscreen games or video, register a zero-height appbar *only* to receive `ABN_FULLSCREENAPP`, or poll `SHQueryUserNotificationState` (`QUNS_BUSY` / `QUNS_RUNNING_D3D_FULL_SCREEN`). Note that "there are no notifications sent when the user starts or stops a full-screen application" for the latter ([SHQueryUserNotificationState](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-shqueryusernotificationstate)), so polling or a foreground-window hook (`SetWinEventHook(EVENT_SYSTEM_FOREGROUND)`, as Rainmeter uses) is needed. *The zero-height appbar trick is folklore; verify in the prototype.*

## 3. Mica / Acrylic

- `DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, &type)` with `DWMSBT_MAINWINDOW` (Mica), `DWMSBT_TRANSIENTWINDOW` (Desktop Acrylic) or `DWMSBT_TABBEDWINDOW` (Mica Alt). **Minimum Windows 11 build 22621 (22H2).** `DWMSBT_AUTO` draws only behind the title bar. Sources: [DWM_SYSTEMBACKDROP_TYPE](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwm_systembackdrop_type), [DWMWINDOWATTRIBUTE](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute).
- Related attributes (all Windows 11 build 22000+): `DWMWA_USE_IMMERSIVE_DARK_MODE` (the material's light/dark tint follows this flag, not the system automatically), `DWMWA_WINDOW_CORNER_PREFERENCE` (rounded corners on a borderless window), `DWMWA_BORDER_COLOR` (`DWMWA_COLOR_NONE` removes the 1px border).
- For the material to show, the client area must be transparent: no painted background, and for webview stacks a transparent webview. Tauri's [`window-vibrancy`](https://github.com/tauri-apps/window-vibrancy/blob/dev/src/windows.rs) does exactly this attribute call on build ≥ 22523. Below that it falls back to the undocumented `SetWindowCompositionAttribute` (acrylic) or `DWMWA_MICA_EFFECT` (build 22000). Its README warns that the pre-22621 acrylic paths have "bad performance when resizing/dragging". Electron has it built in: `win.setBackgroundMaterial('mica'|'acrylic'|'tabbed')`, "only supported on Windows 11 22H2 and up" ([Electron BrowserWindow](https://www.electronjs.org/docs/latest/api/browser-window)). WinUI 3: `Window.SystemBackdrop = MicaBackdrop / DesktopAcrylicBackdrop` ([System backdrops](https://learn.microsoft.com/en-us/windows/apps/develop/ui/system-backdrops)).
- **When it turns solid** ([Mica](https://learn.microsoft.com/en-us/windows/apps/design/style/mica), [Acrylic](https://learn.microsoft.com/en-us/windows/apps/design/style/acrylic)): Transparency effects off, Battery Saver, low-end hardware, High Contrast, and, for Mica and background acrylic, **when the window deactivates**. A desktop widget is inactive almost all the time, so by default it will usually show the solid fallback colour. Mitigations:
  - Windows App SDK `MicaController` / `DesktopAcrylicController` with a `SystemBackdropConfiguration` whose `IsInputActive` the app sets itself; the docs' sample sets it from window activation, and setting it to `true` permanently is the obvious lever (works for any Win32 HWND via the Windows App SDK, including unpackaged apps; see [Apply Mica in Win32 apps](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/ui/apply-mica-win32)). This adds the Windows App Runtime dependency and some RAM. *Prototype needed.*
  - Or design the inactive look to be the solid/tinted fallback, with the material appearing when the user brings the widget forward.
- Mica "only samples the desktop wallpaper once", which makes it cheap. Acrylic is "GPU-intensive" and meant for transient surfaces, so it fits the Dock's pop-down better than the resting widget.

## 4. Theme, accent and wallpaper colour

| Need | API | Change notification | Reachable from |
| --- | --- | --- | --- |
| Dark/light | `UISettings.GetColorValue(UIColorType.Foreground)` then the brightness check in [Support Dark and Light themes in Win32 apps](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/ui/apply-windows-themes). Common alternative: registry `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\AppsUseLightTheme` (DWORD). Tao reads this and notes the undocumented `uxtheme#132 ShouldAppsUseDarkMode` "may return incorrect values on Windows 11" ([tao `dark_mode.rs`](https://github.com/tauri-apps/tao/blob/dev/src/platform_impl/windows/dark_mode.rs)). | `UISettings.ColorValuesChanged` ([docs](https://learn.microsoft.com/en-us/uwp/api/windows.ui.viewmanagement.uisettings.colorvalueschanged)); or `WM_SETTINGCHANGE` with `lParam = "ImmersiveColorSet"` | WinRT from Rust/C#/C++; registry from anything. Electron: `nativeTheme`. |
| Accent colour | `UISettings.GetColorValue(UIColorType.Accent / AccentLight1-3 / AccentDark1-3)` ([UIColorType](https://learn.microsoft.com/en-us/uwp/api/windows.ui.viewmanagement.uicolortype)) | `ColorValuesChanged` | Same; Electron: `systemPreferences.getAccentColor()` |
| Wallpaper image | `IDesktopWallpaper::GetWallpaper(monitorId)` (per monitor), `GetPosition`, `GetSlideshow` ([IDesktopWallpaper](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-idesktopwallpaper), Windows 8+); or `SystemParametersInfo(SPI_GETDESKWALLPAPER)` for one path ([SystemParametersInfo](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-systemparametersinfow)) | No dedicated event. `WM_SETTINGCHANGE` (wParam `SPI_SETDESKWALLPAPER`) is broadcast when a setter uses `SPIF_SENDCHANGE`, which Settings does in practice. Slideshow/Spotlight changes: *unverified whether they broadcast; plan a light re-check (e.g. path + file mtime on a timer or on widget show).* | COM from Rust/C#/C++; native addon for Electron |
| Wallpaper solid colour | `IDesktopWallpaper::GetBackgroundColor` (used when no image is set or as letterbox border) | as above | as above |
| "Colour of the wallpaper" | No OS API. Load the image (downscale to e.g. 64×64) and compute a dominant/average colour in-app, then derive a palette with contrast checks. | re-run on the wallpaper change above | any |

Practical shortcut: Mica already tints itself from the wallpaper, and the accent colour is often set to "automatic from background". `Accent` + Mica may give most of the wallpaper-adaptive look without image analysis.

## Pitfalls checklist

- **Multi-monitor:** use `MonitorFromWindow` + `GetMonitorInfo` (`rcMonitor` for the Dock, `rcWork` for the widget), never `SM_CXSCREEN`/`SPI_GETWORKAREA` (primary-monitor only). Re-layout on `WM_DISPLAYCHANGE`. `IDesktopWallpaper` gives a wallpaper per monitor.
- **Per-monitor DPI:** declare Per-Monitor v2 awareness in the manifest; handle `WM_DPICHANGED` by applying the suggested rect in `lParam` with `SetWindowPos`, or the window will look half or double size on the other monitor ([WM_DPICHANGED](https://learn.microsoft.com/en-us/windows/win32/hidpi/wm-dpichanged)). Webview stacks mostly handle this, but the Dock's pixel height must be recomputed per monitor.
- **Fullscreen apps:** a topmost Dock must hide or drop when a fullscreen app runs. See §2 for detection.
- **Explorer restart:** register `RegisterWindowMessage("TaskbarCreated")`. It is broadcast to top-level windows when the taskbar is recreated, and "on Windows 10, the taskbar also broadcasts this message when the DPI of the primary display changes" ([The Taskbar](https://learn.microsoft.com/en-us/windows/win32/shell/taskbar#taskbar-creation-notification)). Re-add the tray icon, re-register any appbar, and re-apply the z-order pin. WorkerW reparenting would need to rebuild everything here.
- **Virtual desktops:** "every window is considered to be part of a virtual desktop" ([IVirtualDesktopManager](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ivirtualdesktopmanager)). The public API can only query or move a window; pinning to all desktops is not public. Electron's `setVisibleOnAllWorkspaces` "does nothing on Windows". *Whether `WS_EX_TOOLWINDOW` windows show on all virtual desktops is commonly reported but undocumented; verify in a prototype.* Fallback: follow the user by calling `MoveWindowToDesktop` when the desktop changes.
- **Win+D / Show desktop:** see Option B.
- **Focus stealing:** `SetForegroundWindow` is restricted. It works when called from a `WM_HOTKEY` handler because the hotkey press counts as user input.

## Open questions for prototype/spec tickets

1. Does an always-inactive widget with `DWMSBT_MAINWINDOW` look acceptable, or do we need the Windows App SDK controller with `IsInputActive = true` (RAM cost)?
2. Win+D behaviour of a bottom-pinned `WS_EX_TOOLWINDOW` window on 24H2/25H2.
3. Visibility of a tool window across virtual desktops.
4. Whether wallpaper slideshow/Spotlight changes broadcast `WM_SETTINGCHANGE`.
