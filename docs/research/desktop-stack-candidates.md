# Desktop stack candidates under a minimal-RAM budget

Research for issue #3 (child of map #1). Researched 2026-10-06.

**Question.** Which desktop stack gives the smallest memory footprint while still allowing a beautiful, smoothly animated Widget on Windows 11? How easily does each one carry a finalized prototype over to the build unchanged?

## TL;DR

- **The WebView2 runtime sets a floor of about 150–250 MB private bytes before your own UI does anything.** It runs browser, GPU, renderer, network, storage and crashpad processes. That floor applies to Tauri, and Electron sits at a similar or higher level with its own Chromium. Native stacks (WinUI 3 NativeAOT, Slint, Avalonia NativeAOT) run in one process and land in the tens of MB.
- **Every candidate can draw a real Mica/Acrylic window backdrop.** DWM does the drawing; the framework only asks for it. Blur *inside* the content (a frosted panel over other content) works out of the box only in web stacks (CSS `backdrop-filter`), WinUI 3 (`AcrylicBrush`), Avalonia and Flutter (`BackdropFilter`).
- **Prototype fidelity.** Only Tauri and Electron let an HTML/CSS prototype ship unchanged. Every other stack needs the look prototype built in its own UI language (XAML, `.slint`, Dart), which the map already allows.
- **Shortlist: (1) WinUI 3 + C# NativeAOT, (2) Tauri 2.** WinUI 3 is the RAM-first pick with native Fluent materials. Tauri is the fidelity and design-velocity pick, but it needs a measured spike to prove it fits the budget.
- **Proposed budget, for sign-off:** Docked idle ≤ 60 MB working set and ≤ 100 MB private bytes summed over every process the app owns. Hotkey to focused Capture box ≤ 100 ms p95. Idle CPU ≈ 0 (≤ 0.1 % averaged over 5 min). Full numbers below.

## How memory is counted (read this first)

Numbers in the wild mix four different metrics, and they are not interchangeable:

| Metric | What it means | Why it matters here |
|---|---|---|
| **Working set (WS)** | Physical RAM pages mapped right now, including pages shared with other processes (DLLs, fonts). | This is what competes for the user's 16 GB. Summing WS across processes double-counts shared pages. |
| **Private working set** | The resident pages only this process uses. | Best single number for "what does this app cost in RAM". |
| **Private bytes (commit)** | Private memory committed. It can be paged out. | This is what the Task Manager "Details > Commit size" column shows. It shows pressure on the pagefile, and it stays high even when the OS has trimmed the WS. |
| **Free-memory delta** | System free RAM before vs. after launch. | Noisy, but it naturally avoids double-counting shared pages. |

A budget should name both **WS** (RAM pressure) and **private bytes** (true commit), summed over **all** processes the app owns, including `msedgewebview2.exe` children.

## Measured on this machine (WebView2 process groups already running)

The user's own PC is running WebView2 runtime 154.0.4258.53. I read the existing WebView2 process groups with `Get-Process` and broke them down per process type. Each group belongs to a real app, not a hello-world, but the per-process floor is still informative. All values are MB.

| Host app | browser | gpu | renderer | network | storage | crashpad | **Group private** | **Group WS** | Host exe private |
|---|---|---|---|---|---|---|---|---|---|
| Time Doctor | 48 | 16 | 137 | 13 | 8 | 3 | **223** | 413 | 77 |
| Windows Search | 44 | 88 | 98 | 13 | 9 | 3 | **256** | 353 | 80 |
| Widgets board | 44 | 127 | 122 | 13 | 11 | 3 | **319** | **45** | 13 |
| Teams | 77 | 151 | 574 | 15 | 10 | 3 | **839** | 234 | 99 |

Takeaways:

- **Fixed WebView2 overhead** (browser + network + storage + crashpad, with no content) is about **70 MB private**. The GPU process adds another **16–150 MB**, and the renderer adds about 100 MB or more for real content.
- **Widgets board** shows that working set can collapse (45 MB WS against 319 MB private) when a WebView is suspended or set to low memory. This is what `MemoryUsageTargetLevel = Low` / `TrySuspend` do ([MS docs](https://learn.microsoft.com/en-us/microsoft-edge/webview2/reference/win32/icorewebview2_19)). It relieves RAM pressure but not commit, and the page has to be swapped back in when the Widget is summoned. That costs hotkey latency.
- Each WebView2 app gets **its own** browser process group per user-data folder ([process model](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/process-model)). Teams and Edge being open does not share *private* memory with us. What does help: binaries are already in memory when Edge's version matches ([WebView2 perf guide](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/performance)), so shared code pages and cold start get cheaper.

## Comparison

Versions as of 2026-10-06: Tauri 2.12.1 (3.0 is in alpha), Electron 44.5, Windows App SDK 2.5.1, Avalonia 12.1.3, Slint 1.18.1, iced 0.14.0, egui 0.36.2. Flutter's Windows desktop target is stable.

| Stack | Idle memory, best available evidence | Processes | Idle CPU | Window backdrop (Mica/Acrylic) | In-content blur & animation | Global hotkey | Tray | Installer / bundle | Prototype carries over 1:1? | Maturity on Windows |
|---|---|---|---|---|---|---|---|---|---|---|
| **Tauri 2** (WebView2) | Hello-world, sum of WS over all processes: **≈307 MB**; free-mem delta **≈187 MB** [E]. This PC's WebView2 groups: **223–319 MB private** (above). | 1 host + 5–6 WebView2 | ~0 if no CSS animation is running | Yes: `Effect::Mica/Acrylic/Tabbed`. Acrylic lags while dragging or resizing on Win11 22000 [T1] | Yes, all of CSS (`backdrop-filter`, transitions, WAAPI), GPU-composited | Official plugin [T2] | Built in (`tray-icon`) | **≈3 MB** app [E]; uses the system WebView2 runtime | **Yes**: the HTML/CSS prototype *is* the UI | High. v2 stable since 2024, v3 in alpha |
| **Electron** | Hello-world sum of WS **≈279 MB**, free-mem delta **≈100 MB** [E] | 1 main + renderer + GPU + utility | ~0 if nothing is animating | Yes: `backgroundMaterial` mica/acrylic/tabbed, Win11 22H2+ [EL1] | Yes, full Chromium CSS | Built in (`globalShortcut`) | Built in (`Tray`) | **≈385 MB** unpacked [E] | **Yes** | Very high |
| **WinUI 3 + C# NativeAOT** | WinUI Islands NativeAOT starter: **9.2 MiB private WS, 62.2 MiB total WS** (JIT: 13.1 / 94.9) after 10 s idle, Win11 ARM64 [W1] ⚠ | 1 | ~0. Composition animations run off the UI thread | **Native**: `MicaBackdrop`, `DesktopAcrylicBackdrop` | Native `AcrylicBrush`, Composition/implicit animations, connected animations | Win32 `RegisterHotKey` (≈20 lines of interop) | Community `H.NotifyIcon.WinUI` [W3]; no first-party API | NativeAOT starter: **14.8 MiB**, 3 files [W1]; WinAppSDK 1.6 claims ~2× smaller self-contained [W2] | No. The prototype must be written in XAML (that is allowed: "look prototype built in the chosen stack's UI technology") | Medium-high. MS is betting on it in 2026 and admits memory work is ongoing [W4] |
| **Avalonia 12 NativeAOT** | No reliable Windows number. A community template claims "~20 MB usage", method not stated [A1] ⚠⚠ | 1 | ~0 | Yes: `TransparencyLevelHint` Mica / AcrylicBlur [A2] | `ExperimentalAcrylicBorder`, composition animations | None built in; Win32 interop | Built-in `TrayIcon` | NativeAOT ≈ 15–20 MB [A1] ⚠ | No (Avalonia XAML) | High. Cross-platform, AOT supported [A3] |
| **Flutter (Windows)** | Hello-world release: sum of WS **≈103 MB**, free-mem delta **≈83 MB** [E] | 1 | Reports of busy idle on desktop are old (2021) ⚠ [F1] | Community `flutter_acrylic` [F2] | `BackdropFilter`, rich implicit animations | Community `hotkey_manager` | Community `tray_manager` | ≈25 MB [E] | No (Dart widgets) | Medium. Desktop plugins are mostly community-maintained |
| **Slint** | No Windows measurement found. On Linux, one app went from egui at 135 MB to Slint's software renderer at 30 MB [S1] ⚠⚠. Runtime < 300 KiB on MCUs (not comparable) | 1 | ~0 (retained mode, partial redraw) | Not built in. Possible via `window-vibrancy` on the winit window, unverified ⚠ | Property animations / states are good. **No blur primitive** found in docs | `global-hotkey` crate [R1] | `tray-icon` crate [R1] | ~1–5 MB | No (`.slint` DSL, with live preview) | Medium-high. 1.x stable, commercial backing, GPL/royalty-free licence options |
| **iced** | No Windows number found. wgpu GPU stack, likely tens of MB ⚠ | 1 | ~0 | `window-vibrancy`, unverified | Basic animation; no blur | `global-hotkey` crate | `tray-icon` crate | small | No (Rust code) | Medium. Pre-1.0 (0.14), API churn |
| **egui** | ~130 MB on Linux with GPU backend [S1] ⚠⚠ | 1 | ~0 in reactive mode (repaints only on input) | `window-vibrancy`, unverified | Immediate-mode look, hard to make "beautiful" | `global-hotkey` crate | `tray-icon` crate | small | No | High for tools, a poor fit for polished consumer UI |

⚠ = single-source or community measurement, or a different platform/metric. ⚠⚠ = method unstated or a non-Windows platform; treat as a rough indication only.

### Notes on the strongest sources

- **[E] Elanis/web-to-desktop-framework-comparison** is the only side-by-side Windows x64 measurement across Electron, Tauri and Flutter. It is CI-generated and updated 10/2026. It reports two metrics: "median of used memory for main process and children" (a WS sum, which over-counts shared pages) and "free memory delta". On Windows it shows **Tauri using no less memory than Electron** for a hello-world (307 vs 279 MB; 187 vs 100 MB), which contradicts the "Tauri is 10× lighter" marketing. Tauri's own issue #5889 [T3] makes the same point: WebView2 is Chromium, so on Windows the gain is bundle size, not RAM.
- Blog benchmarks claiming "Tauri 42 MB vs Electron 168 MB" (e.g. tech-insider.org, pkgpulse) do not say whether they count `msedgewebview2.exe` children. **I discarded them.**
- **[W1]** comes from a community NuGet package author (WinUI Islands), on ARM64. Microsoft publishes no idle-memory figure for WinUI 3. Treat [W1] as an order of magnitude only, and confirm it in the spike.

## Startup and hotkey latency

- Cold start (Windows x64, release) [E]: Electron ≈150 ms, Tauri ≈505–576 ms, Flutter ≈1.6 s. These are first-window times.
- The Widget never cold-starts on the hotkey. It is launched at login and stays resident. The hotkey only shows an already-created, hidden window and focuses the Capture box, so every stack can manage 1–2 frames (≈16–33 ms) **as long as the UI is still resident**.
- The trap for web stacks: dropping a WebView2 to `MemoryUsageTargetLevel=Low` or `TrySuspend` while Docked saves working set, but the first paint after the hotkey may have to page memory back in. "Performance might be impacted" ([MS docs](https://learn.microsoft.com/en-us/microsoft-edge/webview2/reference/win32/icorewebview2_19)). This is the main trade-off a Tauri spike must measure.
- Target: ≤ 100 ms feels instantaneous (Nielsen's 0.1 s limit, [NN/g](https://www.nngroup.com/articles/response-times-3-important-limits/)).

## Proposed budget (for the user to sign off)

All numbers are summed over **every** process the app owns. Measure with `Get-Process` / Process Explorer on the user's machine, release build, Win11, after 5 min idle.

| State | Working set | Private bytes | CPU |
|---|---|---|---|
| **Docked, idle** (collapsed to the top-edge Dock) | **≤ 60 MB** (hard cap 100) | **≤ 100 MB** (hard cap 150) | **≤ 0.1 %** avg over 5 min; no timers or animation loops while nothing moves |
| **Expanded, 50 Tasks, idle** | ≤ 100 MB (hard cap 150) | ≤ 150 MB (hard cap 220) | ≤ 0.1 % |
| **During expand/collapse animation** | — | — | 60 fps with no dropped frames on a mid-range iGPU; back to idle CPU within 1 s |

| Latency | Target |
|---|---|
| Global hotkey → Capture box visible **and focused** (warm, after 10 min Docked) | **≤ 100 ms p95** |
| Dock → expanded Widget animation | 150–250 ms motion; first frame ≤ 50 ms after click |
| Cold start at login → Dock visible | ≤ 1.5 s (not user-blocking) |
| Installer download | ≤ 20 MB |

What the budget implies: native stacks should clear it comfortably. **Tauri can hit the working-set targets only with WebView2 memory-target tricks, and probably cannot hit the private-bytes targets.** If the user wants Tauri for prototype fidelity, the private-bytes caps would need to rise to roughly 250 MB. That is the explicit trade-off to sign off on.

## Shortlist

### 1. WinUI 3 (Windows App SDK 2.x) + C# NativeAOT: the RAM-first pick

- One process, and the only stack where Mica/Acrylic, Fluent controls, composition animations and Windows 11 visual language are the **native** path, not an add-on.
- The best available evidence puts it in the tens of MB (9 MiB private WS / 62 MiB total WS for a NativeAOT starter [W1]). Startup and size gains come from NativeAOT [W2].
- Costs: Windows-only (the map targets Windows only, so fine). Tray needs a community package. The hotkey needs a few lines of Win32 interop. The look prototype must be built in XAML (allowed by the map's fidelity rule). Microsoft acknowledged WinUI memory bloat in 2026 and says fixes are landing [W4], so pin a recent WinAppSDK.

### 2. Tauri 2: the fidelity and design-velocity pick

- The HTML/CSS prototype ships **unchanged**, which is the strongest possible guarantee for the map's prototype-fidelity rule. CSS gives the richest animation and in-content blur toolset of any candidate. Native Mica/Acrylic window backdrop, official global-shortcut plugin, built-in tray, ≈3 MB app.
- Costs: the WebView2 floor (≈150–250 MB private across 5–6 processes on this very machine). It likely misses the private-bytes budget unless the budget is raised. It needs `MemoryUsageTargetLevel=Low` while Docked and a measured check that the hotkey path stays ≤ 100 ms.

### Why not the others

- **Electron**: same Chromium cost as Tauri plus its own copy of Chromium (≈385 MB on disk). Its only advantage over Tauri is faster cold start, which doesn't matter for a resident widget.
- **Slint**: the strongest RAM contender, with a polished DSL and live preview. It loses on looks: no blur primitive and no built-in Mica/Acrylic, so the "blurred backdrop" requirement is unproven. It is the runner-up if WinUI disappoints in measurement.
- **Avalonia**: capable (Mica/Acrylic, tray, AOT), but no trustworthy Windows memory numbers, and its value (cross-platform) is out of scope here.
- **Flutter**: about 100 MB hello-world, and tray, hotkey and acrylic are all community plugins. It brings no advantage over WinUI for a Windows-only app.
- **iced / egui**: pre-1.0 churn (iced) or a tool-like immediate-mode look (egui). Neither has a blur story.

## Recommended next step

Before choosing between the two, run a **two-hour spike**: build a hidden-window + hotkey + Mica hello-world in both WinUI 3 NativeAOT and Tauri 2, release build. Record WS and private bytes summed over all processes (Docked idle for 5 min, then Expanded), plus hotkey-to-focus latency, on the user's PC under their normal ~82 % RAM load. This turns the ⚠ rows above into first-party numbers.

## Sources

- [E] Elanis, web-to-desktop-framework-comparison (CI benchmarks, updated 10/2026): https://github.com/Elanis/web-to-desktop-framework-comparison
- [T1] Tauri `Effect` enum, Mica/Acrylic/Blur notes: https://docs.rs/tauri/latest/tauri/window/enum.Effect.html
- [T2] Tauri global-shortcut plugin: https://v2.tauri.app/plugin/global-shortcut/
- [T3] tauri-apps/tauri#5889, "Memory benchmark might be incorrect": https://github.com/tauri-apps/tauri/issues/5889
- WebView2 process model: https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/process-model
- WebView2 performance best practices: https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/performance
- WebView2 `MemoryUsageTargetLevel`: https://learn.microsoft.com/en-us/microsoft-edge/webview2/reference/win32/icorewebview2_19
- [EL1] Electron BrowserWindow `setBackgroundMaterial`: https://www.electronjs.org/docs/latest/api/browser-window
- [W1] WinUIIslands NuGet readme (NativeAOT vs JIT memory and size, ARM64): https://www.nuget.org/packages/WinUIIslands/1.1.0
- [W2] What's new in Windows App SDK 1.6 (Native AOT): https://blogs.windows.com/windowsdeveloper/2024/09/04/whats-new-in-windows-app-sdk-1-6/
- Windows App SDK release channels (2.5.1 current): https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/release-channels
- [W3] H.NotifyIcon.WinUI: https://www.nuget.org/packages/H.NotifyIcon.WinUI/
- [W4] PCWorld on Microsoft's 2026 WinUI memory statement (secondary; no MS figures given): https://www.pcworld.com/article/3199779/microsoft-admits-its-windows-11-apps-hog-ram-a-fix-is-coming.html
- [A1] AvaloniaAOT template (unverified claims): https://github.com/lixinyang123/AvaloniaAOT
- [A2] Avalonia `WindowTransparencyLevel`: https://docs.avaloniaui.net/api/avalonia/controls/windowtransparencylevel
- [A3] Avalonia Native AOT: https://docs.avaloniaui.net/docs/deployment/native-aot
- [F1] Uni Stuttgart thesis (2021) on Flutter desktop CPU/GPU (old): https://elib.uni-stuttgart.de/bitstream/11682/11740/1/zindl_ba_final2021.pdf
- [F2] flutter_acrylic: https://github.com/alexmercerind/flutter_acrylic
- [S1] "How I took my Rust GUI from 135 MB to 30 MB" (Linux, egui → Slint software renderer; republished copy): https://neura.market/directories/deepseek/blog/devto-3847725
- [R1] tauri-apps `window-vibrancy`, `global-hotkey`, `tray-icon` crates (framework-agnostic Rust): https://github.com/tauri-apps/window-vibrancy, https://github.com/tauri-apps/global-hotkey, https://github.com/tauri-apps/tray-icon
- Startup/input-lag comparison of Tauri, iced, egui (Linux, 2023): http://lukaskalbertodt.github.io/2023/02/03/tauri-iced-egui-performance-comparison.html
- Nielsen Norman Group, response-time limits: https://www.nngroup.com/articles/response-times-3-important-limits/
