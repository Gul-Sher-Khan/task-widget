; Task Widget per-user installer. Compile twice:
;   ISCC /DAppVersion=1.2.3 /DArch=x64   /DPublishDir=<publish> TaskWidget.iss
;   ISCC /DAppVersion=1.2.3 /DArch=arm64 /DPublishDir=<publish> TaskWidget.iss
; AppVersion is the git tag without the leading v. Do not hard-code it here.

#ifndef AppVersion
  #error Pass /DAppVersion from the git tag, without the leading v.
#endif
#ifndef Arch
  #error Pass /DArch=x64 or /DArch=arm64.
#endif
#ifndef PublishDir
  #error Pass /DPublishDir pointing at the NativeAOT publish folder.
#endif
#if (Arch != "x64") && (Arch != "arm64")
  #error Arch must be x64 or arm64.
#endif
#if Ver < 0x07000000
  #error This script requires Inno Setup 7.
#endif

#define AppName "Task Widget"
#define AppExeName "TaskWidget.exe"
#define AppPublisher "Gul-Sher-Khan"
#define AppURL "https://github.com/Gul-Sher-Khan/task-widget"
#define VerCore AppVersion
#if Pos("-", AppVersion) > 0
  #define VerCore Copy(AppVersion, 1, Pos("-", AppVersion) - 1)
#endif
#define AppVersionNumeric VerCore + ".0"

[Setup]
AppId={{31C9C697-31F9-4A4D-A9ED-EF8946203EB6}}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}/issues
AppUpdatesURL={#AppURL}/releases
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=
MinVersion=10.0.22000
; Each installer only runs where its build runs: the ARM64 app is not an x64 app.
#if Arch == "arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#else
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#endif
OutputBaseFilename=TaskWidget-{#AppVersion}-{#Arch}
OutputDir=..\artifacts\installers
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\src\TaskWidget.Shell\Assets\TaskWidget.ico
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}
CloseApplications=yes
RestartApplications=no
VersionInfoVersion={#AppVersionNumeric}
VersionInfoProductVersion={#AppVersionNumeric}
VersionInfoTextVersion={#AppVersion}
VersionInfoProductTextVersion={#AppVersion}
VersionInfoCompany={#AppPublisher}
VersionInfoProductName={#AppName}
VersionInfoDescription={#AppName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
WindowsVersionNotSupported=Task Widget requires Windows 11.
FinishedLabel=Setup has finished installing Task Widget.
ClickFinish=Click Finish to start Task Widget. It opens at the top right of your screen. After that, tap Ctrl+Shift anywhere to capture a thought.

[Files]
; The self-contained Windows App SDK layout also copies AI, search, widgets, and WebView2.
; This app does not call them. Shipping them puts the installer over 20 MB.
; Keep the .mui language resources: without them WinUI crashes at start-up (0xC000027B) about half the time.
Source: "{#PublishDir}/*"; DestDir: "{app}"; Excludes: "*.pdb,*.xml,*.winmd,onnxruntime.dll,DirectML.dll,NPUDetect.dll,PerceptiveStreaming.dll,WebView2Loader.dll,workloads*.json,Microsoft.Asg.*,Microsoft.Windows.AI.*,Microsoft.Windows.Search.*,Microsoft.Windows.SemanticSearch*,Microsoft.Windows.Widgets.*,Microsoft.Windows.Workloads*,Microsoft.Web.WebView2*,Microsoft.Graphics.Imaging*,Microsoft.Windows.Vision*,Microsoft.Windows.BadgeNotifications*,PushNotifications*,Microsoft.Windows.Management.Deployment*,Microsoft.Windows.Media.Capture*,Microsoft.Windows.Storage*,Microsoft.Windows.Security*,Microsoft.Windows.Internal*,Microsoft.Windows.Private*,Microsoft.Windows.Globalization*"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"

[Code]
const
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';
  RunValueName = 'Task Widget';

var
  StartupCheck: TNewCheckBox;
  Upgrading: Boolean;

function QuotedAppExe: String;
begin
  Result := '"' + ExpandConstant('{app}\{#AppExeName}') + '"';
end;

function InstalledBefore: Boolean;
var
  UninstallString: String;
begin
  // Literal uninstall key. ExpandConstant would read the AppId braces as a constant.
  Result := RegQueryStringValue(HKCU,
    'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#SetupSetting("AppId")}_is1',
    'UninstallString', UninstallString);
end;

function InitializeSetup: Boolean;
begin
  Upgrading := InstalledBefore;

#if Arch == "x64"
  if IsArm64 then
  begin
    SuppressibleMsgBox('This installer is for x64 Windows. On an ARM64 PC, use the ARM64 installer.',
      mbError, MB_OK, IDOK);
    Result := False;
    Exit;
  end;
#endif
#if Arch == "arm64"
  if not IsArm64 then
  begin
    SuppressibleMsgBox('This installer is for ARM64 Windows. On an x64 PC, use the x64 installer.',
      mbError, MB_OK, IDOK);
    Result := False;
    Exit;
  end;
#endif

  Result := True;
end;

procedure InitializeWizard;
begin
  if WizardSilent then
    Exit;

  StartupCheck := TNewCheckBox.Create(WizardForm);
  StartupCheck.Parent := WizardForm.FinishedPage;
  StartupCheck.Caption := 'Start Task Widget when I sign in';
  if Upgrading then
    StartupCheck.Checked := RegValueExists(HKCU, RunKey, RunValueName)
  else
    StartupCheck.Checked := True;
  StartupCheck.Left := WizardForm.FinishedLabel.Left;
  StartupCheck.Top := WizardForm.FinishedLabel.Top + WizardForm.FinishedLabel.Height + ScaleY(16);
  StartupCheck.Width := WizardForm.FinishedPage.ClientWidth - StartupCheck.Left - ScaleX(16);
  StartupCheck.Height := ScaleY(20);
end;

procedure WriteRunValue;
begin
  RegWriteStringValue(HKCU, RunKey, RunValueName, QuotedAppExe);
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Code: Integer;
begin
  { Clicking Finish starts Task Widget. A silent install doesn't: the updater
    restarts the app itself, and winget leaves starting it to the user. }
  if (CurStep = ssDone) and not WizardSilent then
  begin
    ExecAsOriginalUser(ExpandConstant('{app}\{#AppExeName}'), '', '', SW_SHOWNORMAL, ewNoWait, Code);
    Exit;
  end;

  if CurStep <> ssPostInstall then
    Exit;

  { A silent install is how winget and the updater run.
    First install follows the checked default. An upgrade leaves the Run value
    alone, so turning it off in Task Manager stays off. }
  if WizardSilent then
  begin
    if not Upgrading then
      WriteRunValue;
  end
  else if StartupCheck.Checked then
    WriteRunValue
  else
    RegDeleteValue(HKCU, RunKey, RunValueName);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if CurUninstallStep = usUninstall then
    RegDeleteValue(HKCU, RunKey, RunValueName);

  if CurUninstallStep <> usPostUninstall then
    Exit;

  { Silent, very silent, and suppressed message boxes return No, so data stays.
    The button default is No as well. }
  if SuppressibleMsgBox('Also delete your tasks and settings?',
    mbConfirmation, MB_YESNO or MB_DEFBUTTON2, IDNO) <> IDYES then
    Exit;

  DataDir := ExpandConstant('{localappdata}\TaskWidget');
  if CompareText(DataDir, ExpandConstant('{app}')) <> 0 then
    DelTree(DataDir, True, True, True);
end;
