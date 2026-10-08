# Writes the three winget manifests for one published release.
# Komac submits the directory. The version is the git tag without the leading v.
param(
    [Parameter(Mandatory = $true)]
    [string] $Version,

    [Parameter(Mandatory = $true)]
    [string] $Tag,

    [Parameter(Mandatory = $true)]
    [string] $Repository,

    [Parameter(Mandatory = $true)]
    [string] $X64Installer,

    [Parameter(Mandatory = $true)]
    [string] $Arm64Installer,

    [Parameter(Mandatory = $true)]
    [string] $ReleaseDate,

    [Parameter(Mandatory = $true)]
    [string] $OutputDir
)

$ErrorActionPreference = 'Stop'

if ($Version -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$') {
    throw "Version '$Version' is not SemVer."
}
if ($ReleaseDate -notmatch '^\d{4}-\d{2}-\d{2}$') {
    throw "ReleaseDate '$ReleaseDate' must be yyyy-MM-dd."
}

$id = 'GulSherKhan.TaskWidget'
$x64Name = Split-Path $X64Installer -Leaf
$arm64Name = Split-Path $Arm64Installer -Leaf
$x64Hash = (Get-FileHash -LiteralPath $X64Installer -Algorithm SHA256).Hash
$arm64Hash = (Get-FileHash -LiteralPath $Arm64Installer -Algorithm SHA256).Hash
$x64Url = "https://github.com/$Repository/releases/download/$Tag/$x64Name"
$arm64Url = "https://github.com/$Repository/releases/download/$Tag/$arm64Name"
$notesUrl = "https://github.com/$Repository/releases/tag/$Tag"

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

$installer = @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.installer.1.12.0.schema.json
PackageIdentifier: $id
PackageVersion: $version
InstallerType: inno
Scope: user
UpgradeBehavior: install
MinimumOSVersion: 10.0.22000.0
InstallModes:
- interactive
- silent
- silentWithProgress
InstallerSwitches:
  Silent: /VERYSILENT /NORESTART
  SilentWithProgress: /SILENT /NORESTART
ReleaseDate: $ReleaseDate
Installers:
- Architecture: x64
  InstallerUrl: $x64Url
  InstallerSha256: $x64Hash
- Architecture: arm64
  InstallerUrl: $arm64Url
  InstallerSha256: $arm64Hash
ManifestType: installer
ManifestVersion: 1.12.0
"@

$locale = @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.defaultLocale.1.12.0.schema.json
PackageIdentifier: $id
PackageVersion: $version
PackageLocale: en-US
Publisher: Gul-Sher-Khan
PublisherUrl: https://github.com/$Repository
PublisherSupportUrl: https://github.com/$Repository/issues
PackageName: Task Widget
PackageUrl: https://github.com/$Repository
License: Proprietary
ShortDescription: Windows 11 desktop widget that ranks Tasks from a spoken or typed Capture.
Moniker: taskwidget
Tags:
- tasks
- widget
ReleaseNotesUrl: $notesUrl
ManifestType: defaultLocale
ManifestVersion: 1.12.0
"@

$versionManifest = @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.version.1.12.0.schema.json
PackageIdentifier: $id
PackageVersion: $version
DefaultLocale: en-US
ManifestType: version
ManifestVersion: 1.12.0
"@

$utf8 = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllText((Join-Path $OutputDir "$id.installer.yaml"), $installer.Trim() + "`n", $utf8)
[System.IO.File]::WriteAllText((Join-Path $OutputDir "$id.locale.en-US.yaml"), $locale.Trim() + "`n", $utf8)
[System.IO.File]::WriteAllText((Join-Path $OutputDir "$id.yaml"), $versionManifest.Trim() + "`n", $utf8)
