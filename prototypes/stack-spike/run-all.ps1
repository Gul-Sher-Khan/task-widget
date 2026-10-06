# PROTOTYPE: runs every spike measurement back to back (~17 min). Three beeps = keystroke phase starts
# in 10 s, hands off the keyboard; one long beep at the very end = done. Results land in results/*.json.
$here = $PSScriptRoot
$winui = "$here\out\winui-aot\SpikeWinUI.exe"
$tauri = "$here\tauri\src-tauri\target\release\spike-tauri.exe"
$runs = @(
    @{ Exe = $winui; Label = 'winui-aot';             Tasks = 50; IdleSeconds = 300; Hotkeys = 20; Trigger = 'combo' }
    @{ Exe = $tauri; Label = 'tauri';                 Tasks = 50; IdleSeconds = 300; Hotkeys = 20; Trigger = 'combo' }
    @{ Exe = $winui; Label = 'winui-aot-tap';         Tasks = 30; IdleSeconds = 15;  Hotkeys = 10; Trigger = 'tap' }
    @{ Exe = $winui; Label = 'winui-aot-mica-active'; Tasks = 30; IdleSeconds = 60;  Hotkeys = 3;  Trigger = 'combo'; Backdrop = 2 }
)
$dbg = Join-Path ([IO.Path]::GetTempPath()) 'stack-spike-debug.log'
Remove-Item $dbg -ErrorAction SilentlyContinue
foreach ($r in $runs) { & "$here\measure.ps1" @r; '' }
[console]::Beep(660, 900)
'--- WinUI foreground path per trigger (from debug log) ---'
Get-Content $dbg -ErrorAction SilentlyContinue | Where-Object { $_ -match 'foreground' } |
    ForEach-Object { ($_ -split ' ', 2)[1] } | Group-Object | ForEach-Object { "$($_.Count)x $($_.Name)" }
