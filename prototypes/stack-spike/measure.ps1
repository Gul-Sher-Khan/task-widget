# PROTOTYPE harness for "Stack memory and animation spike". Throwaway.
# Launches a spike, lets it sit Docked, then drives the global hotkey and measures:
#   - memory summed over the whole process tree (working set, private working set, private bytes/commit)
#   - CPU over the idle window
#   - hotkey -> first rendered frame with the Capture box focused (QPC, same clock as the app's log)
# Usage: pwsh ./measure.ps1 -Exe <path> -Label winui-aot [-Tasks 30] [-IdleSeconds 300] [-Hotkeys 20] [-Backdrop 0] [-Trigger combo|tap]
# combo = Ctrl+Alt+T, tap = press and release Ctrl+Shift
param(
    [Parameter(Mandatory)] [string] $Exe,
    [Parameter(Mandatory)] [string] $Label,
    [int] $Tasks = 30,
    [int] $IdleSeconds = 300,
    [int] $Hotkeys = 20,
    [int] $Backdrop = 0,
    [ValidateSet('tap', 'combo')] [string] $Trigger = 'combo'
)
$ErrorActionPreference = 'Stop'

Add-Type @'
using System.Runtime.InteropServices;
public static class Keys {
    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, System.UIntPtr extra);
    static void Down(byte vk) { keybd_event(vk, 0, 0, System.UIntPtr.Zero); }
    static void Up(byte vk) { keybd_event(vk, 0, 2, System.UIntPtr.Zero); }
    public static void Hotkey() { Down(0x11); Down(0x12); Down(0x54); Up(0x54); Up(0x12); Up(0x11); }
    public static void Tap() { Down(0xA2); Down(0xA0); Up(0xA0); Up(0xA2); }
    public static void Esc() { Down(0x1B); Up(0x1B); }
}
'@

$log = Join-Path ([IO.Path]::GetTempPath()) 'stack-spike-latency.log'
Remove-Item $log -ErrorAction SilentlyContinue
$freq = [Diagnostics.Stopwatch]::Frequency

function Get-Tree([int] $rootId) {
    $all = Get-CimInstance Win32_Process | Select-Object ProcessId, ParentProcessId, Name
    $ids = [Collections.Generic.HashSet[int]]::new(); [void]$ids.Add($rootId)
    do {
        $added = 0
        foreach ($p in $all) { if ($ids.Contains([int]$p.ParentProcessId) -and $ids.Add([int]$p.ProcessId)) { $added++ } }
    } while ($added)
    ,$ids
}

function Measure-Tree([int] $rootId) {
    $ids = Get-Tree $rootId
    $perf = Get-CimInstance Win32_PerfRawData_PerfProc_Process | Where-Object { $ids.Contains([int]$_.IDProcess) }
    $procs = $ids | ForEach-Object { Get-Process -Id $_ -ErrorAction SilentlyContinue }
    [pscustomobject]@{
        Processes       = @($procs).Count
        WorkingSetMB    = [math]::Round(($procs | Measure-Object WorkingSet64 -Sum).Sum / 1MB, 1)
        PrivateWSMB     = [math]::Round(($perf | Measure-Object WorkingSetPrivate -Sum).Sum / 1MB, 1)
        PrivateBytesMB  = [math]::Round(($procs | Measure-Object PrivateMemorySize64 -Sum).Sum / 1MB, 1)
        CpuSeconds      = ($procs | Measure-Object { $_.TotalProcessorTime.TotalSeconds } -Sum).Sum
        Names           = (($procs | Group-Object ProcessName | ForEach-Object { "$($_.Name)x$($_.Count)" }) -join ', ')
    }
}

function Wait-Mark([int] $before) {
    $deadline = [Diagnostics.Stopwatch]::StartNew()
    while ($deadline.ElapsedMilliseconds -lt 2000) {
        if (Test-Path $log) {
            $lines = @(Get-Content $log)
            if ($lines.Count -gt $before) { return $lines[-1] }
        }
        Start-Sleep -Milliseconds 5
    }
    $null
}

$env:SPIKE_TASKS = $Tasks
$env:SPIKE_BACKDROP = $Backdrop
$app = Start-Process $Exe -PassThru
Write-Host "[$Label] started pid $($app.Id); idling Docked for $IdleSeconds s..."
Start-Sleep 5

$idleStart = Measure-Tree $app.Id
$clock = [Diagnostics.Stopwatch]::StartNew()
$trend = @()
while ($clock.Elapsed.TotalSeconds -lt $IdleSeconds) {
    Start-Sleep ([math]::Min(30, [math]::Max(1, $IdleSeconds - $clock.Elapsed.TotalSeconds)))
    $m = Measure-Tree $app.Id
    $trend += "{0,4}s WS {1} / privWS {2} / private {3} MB" -f [int]$clock.Elapsed.TotalSeconds, $m.WorkingSetMB, $m.PrivateWSMB, $m.PrivateBytesMB
}
$idle = Measure-Tree $app.Id
$cores = [Environment]::ProcessorCount
$idleCpuPct = [math]::Round(100 * ($idle.CpuSeconds - $idleStart.CpuSeconds) / ($clock.Elapsed.TotalSeconds * $cores), 3)

Write-Host "[$Label] keystroke phase in 10 s - hands off the keyboard"; 1..3 | ForEach-Object { [console]::Beep(880, 200); Start-Sleep -Milliseconds 150 }; Start-Sleep 10
$latencies = @(); $expanded = $null
for ($i = 1; $i -le $Hotkeys; $i++) {
    $before = if (Test-Path $log) { @(Get-Content $log).Count } else { 0 }
    $t0 = [Diagnostics.Stopwatch]::GetTimestamp()
    if ($Trigger -eq 'tap') { [Keys]::Tap() } else { [Keys]::Hotkey() }
    $mark = Wait-Mark $before
    if ($mark) {
        $focused = [long]($mark -split ' ')[0]
        $latencies += [math]::Round(($focused - $t0) * 1000.0 / $freq, 1)
    } else { $latencies += $null; Write-Warning "hotkey ${i}: no focus mark within 2 s" }
    Start-Sleep -Milliseconds 700
    if ($i -eq $Hotkeys) { Start-Sleep 3; $expanded = Measure-Tree $app.Id }
    [Keys]::Esc()
    Start-Sleep -Milliseconds 900
}

(Get-Tree $app.Id) | ForEach-Object { $_ } | ForEach-Object { Stop-Process -Id $_ -Force -ErrorAction SilentlyContinue }

$warm = @($latencies | Select-Object -Skip 1 | Where-Object { $_ -ne $null } | Sort-Object)
$p95 = if ($warm.Count) { $warm[[math]::Min($warm.Count - 1, [math]::Ceiling(0.95 * $warm.Count) - 1)] } else { $null }
$result = [ordered]@{
    label = $Label; trigger = $Trigger; exe = $Exe; tasks = $Tasks; backdrop = $Backdrop; idleSeconds = $IdleSeconds
    docked = $idle; dockedCpuPct = $idleCpuPct; expanded = $expanded
    firstHotkeyMs = $latencies[0]; warmP50Ms = $warm[[int][math]::Floor($warm.Count / 2)]; warmP95Ms = $p95
    warmMaxMs = ($warm | Select-Object -Last 1); latenciesMs = $latencies; trend = $trend
    when = (Get-Date).ToString('s'); systemMemoryLoadPct = [math]::Round(100 - 100 * (Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory / (Get-CimInstance Win32_OperatingSystem).TotalVisibleMemorySize, 0)
}
$out = Join-Path $PSScriptRoot "results/$Label.json"
New-Item -ItemType Directory -Force (Split-Path $out) | Out-Null
$result | ConvertTo-Json -Depth 4 | Set-Content $out
"[$Label] docked: WS $($idle.WorkingSetMB) / privWS $($idle.PrivateWSMB) / private $($idle.PrivateBytesMB) MB, CPU $idleCpuPct %, $($idle.Processes) procs ($($idle.Names))"
"[$Label] expanded ($Tasks Tasks): WS $($expanded.WorkingSetMB) / privWS $($expanded.PrivateWSMB) / private $($expanded.PrivateBytesMB) MB"
"[$Label] hotkey->focused: first $($latencies[0]) ms, warm p50 $($result.warmP50Ms) / p95 $p95 / max $($result.warmMaxMs) ms"
"[$Label] system memory load during run: $($result.systemMemoryLoadPct) %"
