using System.Diagnostics;
using System.Security.Cryptography;
using TaskWidget.Core;

namespace TaskWidget;

public sealed class SystemBrowser : IBrowserLauncher
{
    public void Launch(Uri authorize) =>
        Process.Start(new ProcessStartInfo(authorize.AbsoluteUri) { UseShellExecute = true });
}

public sealed class DpapiProtector : IDataProtector
{
    public byte[] Protect(byte[] data) =>
        ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);

    public byte[] Unprotect(byte[] data) =>
        ProtectedData.Unprotect(data, null, DataProtectionScope.CurrentUser);
}
