// PROTOTYPE: throwaway stack spike for "Stack memory and animation spike". Not production code.
// Same behaviour as the WinUI spike: Docked strip pinned to the bottom of the z-order, Ctrl+Alt+T expands,
// raises and focuses the Capture box, Esc docks, F2 cycles the backdrop. Env: SPIKE_TASKS, SPIKE_BACKDROP.
// Appends "<qpc-now> <qpc-at-hotkey>" to %TEMP%\stack-spike-latency.log on the first frame with the
// Capture box focused and the window in the foreground.
#![windows_subsystem = "windows"]

use std::io::Write;
use std::sync::atomic::{AtomicBool, AtomicI64, AtomicU64, Ordering};
use std::time::{Duration, Instant};

use tauri::utils::config::WindowEffectsConfig;
use tauri::window::{Effect, EffectsBuilder};
use tauri::{AppHandle, Emitter, Manager, PhysicalPosition, PhysicalSize, WebviewWindow};
use tauri_plugin_global_shortcut::{Code, GlobalShortcutExt, Modifiers, Shortcut, ShortcutState};
use windows_sys::Win32::System::Performance::QueryPerformanceCounter;
use windows_sys::Win32::UI::WindowsAndMessaging::{
    GetForegroundWindow, SetWindowPos, HWND_BOTTOM, SWP_NOACTIVATE, SWP_NOMOVE, SWP_NOSIZE,
};

const W: f64 = 380.0;
const DOCK_H: f64 = 72.0;
const EXP_H: f64 = 620.0;

static HOTKEY_QPC: AtomicI64 = AtomicI64::new(0);
static EXPANDED: AtomicBool = AtomicBool::new(false);
static ANIM_GEN: AtomicU64 = AtomicU64::new(0);

fn qpc() -> i64 {
    let mut v = 0i64;
    unsafe { QueryPerformanceCounter(&mut v) };
    v
}

fn hwnd(win: &WebviewWindow) -> *mut core::ffi::c_void {
    win.hwnd().map(|h| h.0 as _).unwrap_or(std::ptr::null_mut())
}

fn pin_bottom(win: &WebviewWindow) {
    unsafe { SetWindowPos(hwnd(win), HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE) };
}

/// Animates the window height from a background thread, like the WinUI spike does per frame.
fn animate(win: WebviewWindow, from: f64, to: f64, ms: f64, then_dock: bool) {
    let gen = ANIM_GEN.fetch_add(1, Ordering::SeqCst) + 1;
    std::thread::spawn(move || {
        let scale = win.scale_factor().unwrap_or(1.0);
        let start = Instant::now();
        loop {
            if ANIM_GEN.load(Ordering::SeqCst) != gen {
                return;
            }
            let p = (start.elapsed().as_secs_f64() * 1000.0 / ms).min(1.0);
            let eased = 1.0 - (1.0 - p).powi(3);
            let h = from + (to - from) * eased;
            let _ = win.set_size(PhysicalSize::new((W * scale) as u32, (h * scale) as u32));
            if p >= 1.0 {
                break;
            }
            std::thread::sleep(Duration::from_millis(8));
        }
        if then_dock {
            let _ = win.emit("docked", ());
            pin_bottom(&win);
        }
    });
}

fn expand(app: &AppHandle) {
    HOTKEY_QPC.store(qpc(), Ordering::SeqCst);
    let Some(win) = app.get_webview_window("main") else { return };
    let _ = win.set_always_on_top(true);
    let _ = win.show();
    let _ = win.set_focus();
    let first = !EXPANDED.swap(true, Ordering::SeqCst);
    let _ = win.emit("expand", first);
    if first {
        animate(win, DOCK_H, EXP_H, 220.0, false);
    }
}

#[tauri::command]
fn config() -> (u32, u32) {
    let env = |k: &str, d: u32| std::env::var(k).ok().and_then(|v| v.parse().ok()).unwrap_or(d);
    (env("SPIKE_TASKS", 30), env("SPIKE_BACKDROP", 0))
}

#[tauri::command]
fn mark_focused(win: WebviewWindow) -> bool {
    if unsafe { GetForegroundWindow() } != hwnd(&win) {
        return false;
    }
    let line = format!("{} {}\n", qpc(), HOTKEY_QPC.load(Ordering::SeqCst));
    let path = std::env::temp_dir().join("stack-spike-latency.log");
    if let Ok(mut f) = std::fs::OpenOptions::new().create(true).append(true).open(path) {
        let _ = f.write_all(line.as_bytes());
    }
    true
}

#[tauri::command]
fn dock(win: WebviewWindow) {
    if EXPANDED.swap(false, Ordering::SeqCst) {
        let _ = win.set_always_on_top(false);
        animate(win, EXP_H, DOCK_H, 180.0, true);
    }
}

/// 0 Mica, 1 Mica Alt (Tabbed), 2 "Mica kept active" (not exposed by Tauri on Windows; same as Mica),
/// 3 Acrylic, 4 Solid. Returns the label shown in the stats line.
#[tauri::command]
fn set_backdrop(win: WebviewWindow, mode: u32) -> &'static str {
    let (effect, label) = match mode {
        0 => (Some(Effect::Mica), "Mica"),
        1 => (Some(Effect::Tabbed), "Mica Alt"),
        2 => (Some(Effect::Mica), "Mica (kept-active n/a)"),
        3 => (Some(Effect::Acrylic), "Acrylic"),
        _ => (None, "Solid"),
    };
    let _ = match effect {
        Some(e) => win.set_effects(EffectsBuilder::new().effect(e).build()),
        None => win.set_effects(Option::<WindowEffectsConfig>::None),
    };
    label
}

fn main() {
    tauri::Builder::default()
        .plugin(
            tauri_plugin_global_shortcut::Builder::new()
                .with_handler(|app, _shortcut, event| {
                    if event.state() == ShortcutState::Pressed {
                        expand(app);
                    }
                })
                .build(),
        )
        .invoke_handler(tauri::generate_handler![config, mark_focused, dock, set_backdrop])
        .setup(|app| {
            let win = app.get_webview_window("main").expect("main window");
            let scale = win.scale_factor().unwrap_or(1.0);
            if let Ok(Some(m)) = win.current_monitor() {
                let wa = m.work_area();
                let x = wa.position.x + wa.size.width as i32 - ((W + 16.0) * scale) as i32;
                let y = wa.position.y + (16.0 * scale) as i32;
                let _ = win.set_position(PhysicalPosition::new(x, y));
            }
            set_backdrop(win.clone(), config().1);
            pin_bottom(&win);
            app.global_shortcut()
                .register(Shortcut::new(Some(Modifiers::CONTROL | Modifiers::ALT), Code::KeyT))?;
            Ok(())
        })
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}
