# Third-party notices

Task Widget is under the MIT License (see `LICENSE`). The installers also carry, or were built with, the components below. Each stays under its own license.

| Component | Used for | License |
|---|---|---|
| [.NET runtime](https://github.com/dotnet/runtime) | Compiled into `TaskWidget.exe` by NativeAOT | MIT |
| [Windows App SDK](https://github.com/microsoft/WindowsAppSDK) (WinUI 3) | The UI runtime, shipped in the install folder | [Microsoft Software License Terms](https://www.nuget.org/packages/Microsoft.WindowsAppSDK) (redistributable) |
| [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) | View-model plumbing | MIT |
| [System.Security.Cryptography.ProtectedData](https://github.com/dotnet/runtime) | DPAPI encryption of the sign-in tokens | MIT |
| [Microsoft.Windows.CsWin32](https://github.com/microsoft/CsWin32) | Win32 declarations, generated at build time | MIT |
| [Inno Setup](https://jrsoftware.org/isinfo.php) | Builds the installers | [Inno Setup License](https://jrsoftware.org/files/is/license.txt) |

Task Widget does not use OpenAI's devkit. The Sign in with ChatGPT flow and the Responses API calls are written by hand.
