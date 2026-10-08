# Compiles one Task Widget installer.
# AppVersion is the git tag without the leading v. The caller supplies it.
param(
    [Parameter(Mandatory = $true)]
    [string] $Version,

    [Parameter(Mandatory = $true)]
    [ValidateSet('x64', 'arm64')]
    [string] $Arch,

    [Parameter(Mandatory = $true)]
    [string] $PublishDir,

    [Parameter(Mandatory = $true)]
    [string] $OutputDir
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

if ($Version -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$') {
    throw "Version '$Version' is not SemVer. Pass the git tag without the leading v."
}

$publishFull = (Resolve-Path -LiteralPath $PublishDir).Path
if (-not (Test-Path -LiteralPath (Join-Path $publishFull 'TaskWidget.exe'))) {
    throw "TaskWidget.exe is missing in $publishFull"
}
foreach ($ext in '*.pri', '*.xbf') {
    if (-not (Get-ChildItem -LiteralPath $publishFull -Filter $ext)) {
        throw "NativeAOT publish is missing $ext in $publishFull. dotnet publish -o drops these unless the project copies them."
    }
}

$iscc = @(
    "$env:ProgramFiles\Inno Setup 7\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 7\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 7\ISCC.exe"
) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $iscc) {
    throw "Inno Setup 7 (ISCC.exe) is not installed."
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
$outputFull = (Resolve-Path -LiteralPath $OutputDir).Path

# Forward slashes: Inno's preprocessor treats \t in a Windows path as a tab.
$publishDefine = $publishFull.Replace('\', '/')
$outputDefine = $outputFull.Replace('\', '/')
$script = Join-Path $PSScriptRoot 'TaskWidget.iss'

& $iscc "/DAppVersion=$Version" "/DArch=$Arch" "/DPublishDir=$publishDefine" "/O$outputDefine" $script
if ($LASTEXITCODE -ne 0) {
    throw "ISCC failed for $Arch with exit code $LASTEXITCODE"
}

$installer = Join-Path $outputFull "TaskWidget-$Version-$Arch.exe"
if (-not (Test-Path -LiteralPath $installer)) {
    throw "ISCC did not write $installer"
}

$item = Get-Item -LiteralPath $installer
$limit = 20MB
if ($item.Length -ge $limit) {
    throw "$($item.Name) is $($item.Length) bytes, which is not under 20 MB."
}

Write-Output "$($item.FullName) $($item.Length)"
