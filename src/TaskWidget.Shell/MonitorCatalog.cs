using System.Runtime.InteropServices;
using Windows.Graphics;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.HiDpi;

namespace TaskWidget;

// Live displays. Identity is the monitor device interface, not \\.\DISPLAYn, which Windows reuses.
static unsafe class MonitorCatalog
{
    static readonly MONITORENUMPROC EnumProc = OnEnum;

    public static List<MonitorSnap> Read()
    {
        var found = new List<MonitorSnap>();
        var handles = new List<HMONITOR>();
        var held = GCHandle.Alloc(handles);
        try
        {
            PInvoke.EnumDisplayMonitors(default, null, EnumProc, (LPARAM)GCHandle.ToIntPtr(held));
        }
        finally
        {
            held.Free();
        }

        foreach (var monitor in handles)
        {
            if (TryRead(monitor) is MonitorSnap snap)
                found.Add(snap);
        }

        return found;
    }

    unsafe static MonitorSnap? TryRead(HMONITOR monitor)
    {
        var info = new MONITORINFOEXW();
        info.monitorInfo.cbSize = (uint)sizeof(MONITORINFOEXW);
        if (!PInvoke.GetMonitorInfo(monitor, (MONITORINFO*)&info))
            return null;

        var gdi = Text(info.szDevice);
        if (gdi.Length == 0)
            return null;

        uint dpi = 0;
        uint dpiY = 0;
        if (PInvoke.GetDpiForMonitor(monitor, MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, &dpi, &dpiY).Failed)
            dpi = 0;

        var screen = info.monitorInfo;
        return new MonitorSnap
        {
            DeviceId = DeviceId(gdi),
            Primary = (screen.dwFlags & 0x1) != 0, // MONITORINFOF_PRIMARY
            Bounds = Rect(screen.rcMonitor),
            Work = Rect(screen.rcWork),
            Dpi = dpi,
        };
    }

    unsafe static string DeviceId(string gdiName)
    {
        var device = new DISPLAY_DEVICEW { cb = (uint)sizeof(DISPLAY_DEVICEW) };
        fixed (char* name = gdiName)
        {
            if (PInvoke.EnumDisplayDevices(name, 0, &device, PInvoke.EDD_GET_DEVICE_INTERFACE_NAME))
            {
                var path = Text(device.DeviceID);
                if (path.Length > 0)
                    return path;
            }

            device = new DISPLAY_DEVICEW { cb = (uint)sizeof(DISPLAY_DEVICEW) };
            if (PInvoke.EnumDisplayDevices(name, 0, &device, 0))
            {
                var id = Text(device.DeviceID);
                if (id.Length > 0)
                    return id;
            }
        }

        return gdiName;
    }

    static unsafe BOOL OnEnum(HMONITOR monitor, HDC hdc, RECT* clip, LPARAM data)
    {
        if (GCHandle.FromIntPtr((nint)data).Target is List<HMONITOR> handles)
            handles.Add(monitor);
        return true;
    }

    static string Text<T>(T value) where T : unmanaged
    {
        var text = value.ToString() ?? "";
        var end = text.IndexOf('\0');
        return end < 0 ? text.Trim() : text[..end].Trim();
    }

    static RectInt32 Rect(RECT rect) => new(rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top);
}

sealed class MonitorSnap
{
    public required string DeviceId { get; init; }
    public bool Primary { get; init; }
    public RectInt32 Bounds { get; init; }
    public RectInt32 Work { get; init; }
    public uint Dpi { get; init; }
}
