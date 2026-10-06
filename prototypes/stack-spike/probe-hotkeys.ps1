# PROTOTYPE: lists which global hotkey combos are free on this machine (registers then releases each; presses nothing).
Add-Type @'
using System.Runtime.InteropServices;
public static class HK {
  [DllImport("user32.dll", SetLastError=true)] public static extern bool RegisterHotKey(System.IntPtr h, int id, uint mods, uint vk);
  [DllImport("user32.dll")] public static extern bool UnregisterHotKey(System.IntPtr h, int id);
}
'@
$mods = [ordered]@{ 'Ctrl+Alt' = 3; 'Ctrl+Shift' = 6; 'Alt+Shift' = 5; 'Win+Alt' = 9; 'Ctrl+Alt+Shift' = 7 }
$keys = [ordered]@{ 'Space' = 0x20; 'T' = 0x54; 'J' = 0x4A; 'K' = 0x4B; 'N' = 0x4E }
$id = 100
$rows = foreach ($m in $mods.GetEnumerator()) {
    foreach ($k in $keys.GetEnumerator()) {
        $id++
        $ok = [HK]::RegisterHotKey([IntPtr]::Zero, $id, $m.Value -bor 0x4000, $k.Value)
        $err = [Runtime.InteropServices.Marshal]::GetLastWin32Error()
        if ($ok) { [void][HK]::UnregisterHotKey([IntPtr]::Zero, $id) }
        "{0,-22} {1}" -f "$($m.Key)+$($k.Key)", $(if ($ok) { 'free' } else { "TAKEN ($err)" })
    }
}
$rows
