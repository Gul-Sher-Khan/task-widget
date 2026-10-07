# Installer, auto-update and signing options

Research for issue #18 (child of map #1). Researched 2026-10-07 against vendor docs and source (Microsoft Learn, Azure pricing, SignPath, Velopack, Inno Setup, WiX, NSIS, WinSparkle, winget-pkgs, CA/Browser Forum). The app is an unpackaged, self-contained WinUI 3 + C# NativeAOT build, x64 and ARM64, Windows 11 only ([ADR 0001](../adr/0001-winui3-nativeaot-stack.md)). Credentials and settings live under `%LOCALAPPDATA%\TaskWidget\` (ADR 0002).

## Bottom line

| Question | Recommendation | Confidence |
| --- | --- | --- |
| Installer | **Inno Setup 7** if the 20 MB download target holds: solid LZMA2 is the only option measured under it (~14–16 MB vs ~25 MB for zip/Velopack). **Velopack** if built-in delta updates are worth a ~25–26 MB first download. | High on sizes (measured), medium on the choice |
| Auto-update | Velopack if Velopack is the installer. With Inno, a hand-written check against GitHub Releases that downloads the next Setup.exe and runs it silently. Skip WinSparkle. None of these keeps a process running when idle. | High |
| Signing | **SignPath Foundation** (free, CI-only, signs as "SignPath Foundation"). Azure Artifact Signing is only $9.99/month but individuals must live in the US or Canada. Ship v0.x unsigned until signing is set up; SignPath wants a released project anyway. | High on facts, medium on SignPath acceptance |
| Distribution | GitHub Releases plus a winget manifest. A Scoop manifest in our own bucket is optional. | High |
| Start with Windows | `HKCU\...\Run` value written by the app's own setting; the installer removes it on uninstall. | High |

## Measured sizes

The spike's publish folder (`prototypes/stack-spike/out/winui-aot`, x64, Windows App SDK self-contained, `.pri`/`.xbf` copied in) is 98 MB on disk. 36 MB of that is `SpikeWinUI.pdb`, which must not ship. Without it: **65.6 MB in 238 files**. Largest files: `Microsoft.ui.xaml.dll` 15.3 MB, `Microsoft.UI.Xaml.Controls.dll` 6.9 MB, `SpikeWinUI.exe` 5.9 MB. The ~100 locale folders (WinAppSDK `.mui` resources) add 3.5 MB.

| Format | Bytes | MB |
| --- | ---: | ---: |
| zip, .NET `CompressionLevel.Optimal` (Deflate, same as Velopack's `.nupkg`) | 25,766,939 | 25.8 |
| zip, .NET `CompressionLevel.SmallestSize` | 24,848,179 | 24.8 |
| tar + xz `-9e` (solid LZMA2, 64 MB dictionary) | 15,768,792 | 15.8 |
| tar + xz `--x86 --lzma2=preset=9e` (adds an x86 branch filter) | 14,202,548 | 14.2 |

Measured 2026-10-07 on the spike output. My zip came out at ~25 MB, not the ~23 MB quoted in the issue; that may be a different zip tool or a MiB figure. Either way **Deflate misses 20 MB and solid LZMA2 clears it by 4–6 MB**. Inno Setup's default is `lzma2/max`, and `SolidCompression=yes` makes it compress everything as one stream ([Inno help source, `isetup.xml`](https://github.com/jrsoftware/issrc/blob/main/ISHelp/isetup.xml)). The site says Setup adds "only 1.78 MB overhead with all features included" ([jrsoftware.org](https://jrsoftware.org/isinfo.php)), so an Inno installer should land at **~16–18 MB**. Not measured: I did not install Inno, NSIS, WiX or `vpk` (that would mean downloading tools). I also did not measure ARM64, because no ARM64 spike build exists. ARM64 code would use xz's ARM64 filter rather than x86.

Possible extra trimming (unverified): `Microsoft.UI.Xaml.winmd` (1.6 MB) may not be needed at runtime by a NativeAOT app, and the locale folders could be cut if the app ships in English only. Both need testing before anyone relies on them.

## 1. Installer technology

MSIX is out. The app is unpackaged, and ADR 0001 rejected MSIX because sideloading needs a trusted certificate. See section 4 for the one case where MSIX would come back: the Microsoft Store.

### Inno Setup 7

- **Version and licence:** 7.1.0 was released 2026-08-12 ([issrc releases](https://github.com/jrsoftware/issrc/releases)). The licence lets anyone use it, commercial use included ([license.txt](https://github.com/jrsoftware/issrc/blob/main/license.txt)); commercial users are "requested" to buy a licence. Free for us.
- **Compression:** LZMA2 with solid compression, the best measured ratio (see above).
- **Per-user, no admin:** with `PrivilegesRequired=lowest`, Setup "will not request to be run with administrative privileges … and will always run in non administrative install mode" (`isetup.xml`). It then installs under the user's profile, and `HKA` registry entries go to HKCU.
- **Refuse Windows 10:** `MinVersion=10.0.22000`. "If the user's system does not meet the minimum version requirement, Setup will give an error message and exit" (`isetup.xml`). A custom "requires Windows 11" message can come from `InitializeSetup` in Pascal Script.
- **x64 + ARM64:** "Extensive support for … x64 and Arm64 architectures" ([isinfo](https://jrsoftware.org/isinfo.php)). Inno 7 can also build 64-bit installers (`SetupArchitecture=x64`). Simplest: build one installer per architecture, or one installer holding both payloads with `Check: IsArm64`.
- **`.pri`/`.xbf`:** Inno packs whatever is in the publish folder. Copying them is a build step (an MSBuild target after publish), not an installer feature.
- **Uninstall cleanup:** `[UninstallDelete] Type: filesandordirs; Name: "{localappdata}\TaskWidget"` removes user data. The uninstaller can ask first ("Also delete your Tasks and sign-in?") in `CurUninstallStepChanged`. A Run-key value written by the installer goes away with `Flags: uninsdeletevalue`. One the app wrote itself needs an explicit delete in `[UninstallDelete]`/code.
- **Signing:** the `SignTool=` directive signs Setup, the uninstaller and Setup's temporary self-copies. The 7.1 notes warn that signing Setup only after compilation leaves those copies unsigned, and Defender ASR rules can then block them ([whatsnew](https://github.com/jrsoftware/issrc/blob/main/whatsnew.htm)).
- **winget:** `InstallerType: inno` is supported natively, and winget supplies the silent switches ([manifest docs](https://learn.microsoft.com/en-us/windows/package-manager/package/manifest)).
- **Auto-update:** none built in (see section 2).

### Velopack

- **Status:** MIT, actively maintained; latest release 1.2.161 on 2026-09-29 ([releases](https://github.com/velopack/velopack/releases)). The C# library declares `IsAotCompatible=true` for net8.0+ ([Velopack.csproj](https://github.com/velopack/velopack/blob/develop/src/lib-csharp/Velopack.csproj)).
- **Install model:** a one-click `Setup.exe` with no wizard that "will extract the application to `%LocalAppData%\{packId}`" and needs no elevation ([installer docs](https://docs.velopack.io/packaging/installer)). The app lives in `{packId}\current\`, so its path stays the same across updates.
- **Compression:** packages are `.nupkg` zip files written with `System.IO.Compression` at `CompressionLevel.Optimal` ([EasyZip.cs](https://github.com/velopack/velopack/blob/develop/src/lib-csharp/Util/EasyZip.cs)). That is Deflate, so the first download is ~25.8 MB plus the Setup stub. **Misses the 20 MB target.**
- **Refuse Windows 10:** put the version in the RID: `vpk pack -r win10.0.22000-x64`. vpk writes it to `osMinVersion` ([PackageBuilder.cs](https://github.com/velopack/velopack/blob/develop/src/vpk/Velopack.Packaging/PackageBuilder.cs)), and Setup fails with `OsVersionRequired` below that build ([install.rs](https://github.com/velopack/velopack/blob/develop/src/bins/src/commands/install.rs)). The same mechanism checks CPU architecture. The message is Velopack's own wording, not ours.
- **Uninstall cleanup:** Update.exe runs the app's uninstall hook, removes shortcuts, then deletes the **whole `%LocalAppData%\{packId}` folder** ([uninstall.rs](https://github.com/velopack/velopack/blob/develop/src/bins/src/commands/uninstall.rs)). It does not ask the user anything. **Clash with ADR 0002:** with `packId = TaskWidget`, the install root is the same folder as our data folder. Data would survive updates (only `current\` is replaced) and be wiped on uninstall without a prompt. To keep the two apart, use a different packId or data path. To clean other locations (Run key, data elsewhere), use `OnBeforeUninstallFastCallback`, which has a 30 s limit and no UI ([integration docs](https://docs.velopack.io/integrating/overview)).
- **Startup:** `VelopackApp.Build().Run()` must be the first call in `Main`. In WinUI 3 that means turning off the XAML-generated `Main` (`DISABLE_XAML_GENERATED_MAIN`) and writing our own.
- **Shortcuts:** Desktop and StartMenuRoot by default. A `Startup` shortcut location exists, but it is fixed at pack time, so it can't serve as a user toggle ([locator.rs](https://github.com/velopack/velopack/blob/develop/src/lib-rust/src/locator.rs)).
- **Signing:** `--signParams` (signtool), `--azureTrustedSignFile` (Artifact Signing) or `--signTemplate` (any tool, `{{file}}`). vpk signs its own Setup/Update binaries during the build ([signing docs](https://docs.velopack.io/packaging/signing)). SignPath works differently: CI uploads the artifact and SignPath signs it, so either vpk has to call SignPath partway through its build, or we sign the app files first and then the final Setup.exe. That needs a prototype.
- **MSI:** `--msi` also builds a machine-wide MSI bootstrapper ([vpk CLI](https://docs.velopack.io/reference/cli/content/vpk-windows)). We don't need it.

### WiX Toolset 7 (MSI)

- v7.0.0 was released 2026-04-06 ([releases](https://github.com/wixtoolset/wix/releases)). The source is MS-RL. Official binaries fall under the Open Source Maintenance Fee EULA, which "applies only to Users that use the Software as part of revenue-generating activities and have an annual gross revenue greater than or equal to US$10,000" ([OSMFEULA.txt](https://github.com/wixtoolset/wix/blob/main/OSMFEULA.txt)). Free for us today.
- Supports per-user MSI (`Scope="perUser"`), ARM64 MSI, and a Windows 11 launch condition. MSI stores files in CAB archives (MSZIP/LZX), not solid LZMA, so expect a size near the zip, not the Inno result (not measured). WiX is the most verbose of the four, and removing a `%LOCALAPPDATA%` data folder needs `util:RemoveFolderEx` or a custom action. **Not recommended:** it costs the most effort and isn't the smallest.

### NSIS 3.13

- Released 2026-09-27, zlib licence ([download](https://nsis.sourceforge.io/Download)). Its stub is smaller than Inno's, and it can use solid LZMA (`SetCompressor /SOLID lzma`), so it ends up about the same size as Inno. Per-user install uses `RequestExecutionLevel user`; the Windows 11 check is a build-number check through `WinVer.nsh`. Uninstall and registry handling are scripted by hand. winget supports `InstallerType: nullsoft`. **Viable**, but Inno gives the same result with less script and has native ARM64/x64 install modes.

### Plain zip

- 25 MB, no Start menu entry, no uninstall entry, nothing that refuses Windows 10 (the app would have to check itself). winget does have a "zip + portable" type. **Use as a secondary portable download only.**

## 2. Auto-update from GitHub Releases

| Option | Deltas | Resident cost when idle | Notes |
| --- | --- | --- | --- |
| **Velopack** | Yes. Zstandard binary patches per file; it decides between a chain of deltas and the full package ([deltas](https://docs.velopack.io/packaging/deltas)) | **No resident process.** The check is in-process code; `Update.exe` runs only to apply an update ([overview](https://docs.velopack.io/integrating/overview)) | `GithubSource` reads the GitHub Releases API ([GithubSource.cs](https://github.com/velopack/velopack/blob/develop/src/lib-csharp/Sources/GithubSource.cs)); `vpk upload github` / `vpk download github` in CI. |
| **Hand-written + Inno** | No. Downloads the full ~16–18 MB Setup.exe | **None** beyond an occasional `HttpClient` call (the app already needs HTTP for the ChatGPT Connection) | Read `GET /repos/{owner}/{repo}/releases/latest` (unauthenticated limit: 60 requests/hour per IP ([docs](https://docs.github.com/en/rest/using-the-rest-api/rate-limits-for-the-rest-api))), compare versions, download the asset, check its signature/hash, run `Setup.exe /VERYSILENT /SUPPRESSMSGBOXES /CLOSEAPPLICATIONS`, exit. About 150 lines. |
| **WinSparkle 0.9.4** (2026-07-21) | No (Sparkle appcast; downloads and runs a full installer) | A native DLL loaded into our process with its own UI toolkit and background check thread (RAM not measured) | x64/ARM64 DLLs, Ed25519-signed updates ([README](https://github.com/vslavik/winsparkle)). Needs an appcast XML hosted somewhere. **Adds RAM and a second UI stack for nothing Velopack or a hand-written check can't do.** |

RAM: none of the three needs a background process or service. The idle cost of Velopack or a hand-written check is the code compiled into the AOT image, plus whatever `HttpClient` holds onto after a check. Measure it with the spike's `measure.ps1` (check, then idle 5 min) before committing. The likely deciding factor is size, not RAM: Velopack's deltas make each later update small, but its first download is ~25 MB.

## 3. Code signing

| Option | Cost | Eligibility | CI | Publisher shown |
| --- | --- | --- | --- | --- |
| **SignPath Foundation** | Free for OSS ([signpath.org](https://signpath.org/)) | OSI licence with no commercial dual licence; actively maintained; **"already be released in the form that should be signed"**; MFA for all members; Author/Reviewer/Approver roles; must offer uninstall; no undisclosed privacy-affecting features ([terms](https://signpath.org/terms)) | `signpath/github-action-submit-signing-request@v3`: upload the artifact with `actions/upload-artifact`, then submit. For OSS, every job leading up to signing must run on GitHub-hosted runners. Origin metadata comes from GitHub and "can therefore not be forged" ([docs](https://docs.signpath.io/trusted-build-systems/github)) | **"SignPath Foundation"**, not the maintainer. Key held in their HSM. |
| **Azure Artifact Signing** (formerly Trusted Signing) | **$9.99/month** Basic (5,000 signatures), $99.99 Premium; $0.005 per extra signature ([product page](https://azure.microsoft.com/en-us/products/artifact-signing)). Needs a paid subscription, no free or trial ([FAQ](https://learn.microsoft.com/en-us/azure/artifact-signing/faq)). Billed per month, not pro rata | **"Individual developers must be located in the United States or Canada."** Organizations in the US, CA, EU, UK, AU, NZ, JP, KR, SG, CH, NO, IL ([quickstart](https://learn.microsoft.com/en-us/azure/artifact-signing/quickstart)). Individuals verify through Microsoft Verified ID (AU10TIX); the Azure billing account must be of type Individual | `Azure/artifact-signing-action` (the renamed trusted-signing-action, active); Velopack `--azureTrustedSignFile`. Short-lived certificates; Microsoft holds the key | Your validated legal name |
| **OV certificate** (e.g. Certum) | Certum Open Source Code Signing: €49 cloud (SimplySign), €69 card set ([Certum shop](https://shop.certum.eu/code-signing.html)). Standard OV: €139–209. Since 2026-03-01 the maximum validity is **460 days**, so expect to renew roughly every 15 months ([CA/B ballot CSC-31](https://cabforum.org/2025/11/17/ballot-csc-31-maximum-validity-reduction)) | Open Source variant for developers who publish free/OSS software; identity check of an individual | The key has to live on a token or cloud HSM. Signing in unattended CI with SimplySign's one-time-code login is awkward (not verified in this research) | Your name |
| **Unsigned** | Free | None | None | "Unknown publisher" |

**SmartScreen** ([Microsoft Learn](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation), updated 2026-08):

- An unsigned file gets "Windows protected your PC" and needs "Run anyway". Every new version starts from zero reputation.
- An OV/EV-signed file is still "flagged as unrecognized until reputation accumulates", but the publisher name is shown, and reputation can carry over to later files signed with the same identity. "EV certificates no longer bypass SmartScreen."
- Building reputation "can take several weeks and hundreds of clean installs." There is no submission process for consumers.
- **Smart App Control** on Windows 11 "will block execution of unsigned files unless the file has a positive reputation", and applies to all executables, not only downloads. This is the strongest argument for signing a Windows-11-only app.
- SignPath signs with one certificate across many OSS projects. Whether that shared publisher already has good SmartScreen reputation is not documented; I didn't verify it.

**For this project:** if the maintainer is outside the US and Canada, Artifact Signing is unavailable for an individual, and a sole maintainer usually has no registered organization in a listed country. That leaves SignPath (free; the publisher reads "SignPath Foundation"; needs the role setup and a first release) or a Certum OSS cert (~€49 per ~15 months, name on the certificate, awkward in CI). SignPath also checks how the app is built and what it does. A low-level keyboard hook plus a network client should be described plainly on the download page so the "no undisclosed privacy features" rule is clearly met.

## 4. Distribution channels

- **GitHub Releases:** always. Upload the installer per architecture, the portable zip, and (with Velopack) `releases.*.json` plus the full and delta `.nupkg` files.
- **winget:** submit a PR to `microsoft/winget-pkgs` with `wingetcreate` or `YAMLCreate.ps1`. Requirements: silent install support, `InstallerUrl` straight from the publisher's release location (GitHub Releases qualifies, redirectors don't), installs and uninstalls cleanly for admins and non-admins, and passes Defender and several antivirus scans ([submission docs](https://learn.microsoft.com/en-us/windows/package-manager/package/repository)). Signing is not required. Inno (`inno`) and NSIS (`nullsoft`) get silent switches for free; Velopack's Setup.exe would be `exe` with its silent switch. Risk: a global low-level keyboard hook could trip a PUA heuristic in the antivirus scan (`Binary-Validation-Error`). Not verified. Later versions can be automated with `wingetcreate update` from a release workflow.
- **Scoop:** a JSON manifest pointing at the portable zip. Our own bucket (a repo) needs nobody's approval. `ScoopInstaller/Extras` needs a proposal issue approved first ([contributing](https://github.com/ScoopInstaller/.github/blob/main/.github/CONTRIBUTING.md)). Scoop updates the app itself, so the in-app updater must turn itself off when the app runs from a Scoop folder. Optional.
- **Microsoft Store (noted, not asked for):** registration is now free for individuals ([Windows blog, 2025-09-10](https://blogs.windows.com/windowsdeveloper/2025/09/10/free-developer-registration-for-individual-developers-on-microsoft-store/)). An EXE/MSI listing still needs the installer and all its PE files signed by a CA in the Microsoft Trusted Root Program, a versioned URL and silent install ([requirements](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msi/app-package-requirements)), so it doesn't avoid signing. A Store **MSIX** would be signed by Microsoft for free and never trigger SmartScreen, but that reopens the "unpackaged" decision in ADR 0001. Flagging it for the map, not recommending it.

## 5. Start with Windows

| Mechanism | Admin? | Notes |
| --- | --- | --- |
| **`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`** | No | Runs at every logon. Command line ≤260 characters, order not guaranteed, may be delayed "to a time when they are less likely to interfere with the foreground user experience" ([Run keys](https://learn.microsoft.com/en-us/windows/win32/setupapi/run-and-runonce-registry-keys)). Listed in Settings › Apps › Startup and in Task Manager. |
| Startup folder shortcut (`shell:startup`) | No | Same delay rules. A `.lnk` file is easier for users to find and delete, but has to be kept in sync on updates. |
| Scheduled task with a logon trigger | Unclear for non-admin (not verified) | Can control delay and run level, but adds COM code or `schtasks` and appears in a less familiar place. Overkill here. |

**Recommendation:** the app's "Start with Windows" setting writes or deletes a value under the HKCU Run key, pointing at the stable exe path (Inno: `{app}\TaskWidget.exe`; Velopack: `%LocalAppData%\{packId}\current\TaskWidget.exe`). The installer can offer an initial checkbox (Inno `[Tasks]` + `[Registry] … Flags: uninsdeletevalue`); the uninstaller always deletes the value (Velopack: in the uninstall hook). The app should not rewrite the value on every launch: when a user disables the entry in Task Manager or Settings, Windows records that in `HKCU\…\Explorer\StartupApproved\Run` (undocumented; widely reported), and rewriting `Run` on each start could undo the user's choice.

## Costs at a glance

| Path | Money | Download | Update size | Effort |
| --- | --- | --- | --- | --- |
| Inno + hand-written updater + SignPath | $0 | ~16–18 MB (est.) | full installer each time | Medium: Inno script, ~150 lines of update check, SignPath onboarding |
| Velopack + SignPath | $0 | ~26–28 MB (est.) | deltas | Low to medium: vpk in CI, custom `Main`, packId/data-path decision; SignPath + vpk signing order needs a prototype |
| Either + Certum OSS cert | ~€49 per ~15 months | same | same | Plus manual or awkward CI signing |
| Either + Artifact Signing | $9.99/month (~$120/year) | same | same | Only if the maintainer qualifies (US/CA individual or org in a listed country) |
| Either, unsigned | $0 | same | same | Users click through SmartScreen; Smart App Control may block outright |

## Open questions for the map

- Is the 20 MB target firm? If yes, Inno (or NSIS). If ~26 MB is acceptable for delta updates, Velopack.
- Does uninstall delete user data silently, or ask first? Velopack can't ask; Inno can.
- Does the maintainer qualify for Artifact Signing (location / organization)? If not, apply to SignPath after the first public (unsigned) release.
- Before choosing, measure in the spike the binary-size and idle-RAM cost of the Velopack library vs a bare `HttpClient` check.
