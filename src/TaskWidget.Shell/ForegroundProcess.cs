using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Threading;

namespace TaskWidget;

static class ForegroundProcess
{
    public static string? FileName()
    {
        var window = PInvoke.GetForegroundWindow();
        if (window.IsNull)
            return null;

        uint processId;
        unsafe
        {
            PInvoke.GetWindowThreadProcessId(window, &processId);
        }

        if (processId == 0)
            return null;

        var raw = PInvoke.OpenProcess(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (raw.IsNull)
            return null;

        using var process = new ProcessHandle(raw);
        Span<char> buffer = stackalloc char[1024];
        uint size = (uint)buffer.Length;
        if (!PInvoke.QueryFullProcessImageName(process, PROCESS_NAME_FORMAT.PROCESS_NAME_WIN32, buffer, ref size) || size == 0)
            return null;

        return Path.GetFileName(buffer[..(int)size].ToString());
    }

    sealed partial class ProcessHandle : SafeHandle
    {
        public ProcessHandle(HANDLE handle)
            : base(nint.Zero, ownsHandle: true)
        {
            SetHandle(handle);
        }

        public override bool IsInvalid => handle == nint.Zero;

        protected override bool ReleaseHandle() => PInvoke.CloseHandle((HANDLE)handle);
    }
}
