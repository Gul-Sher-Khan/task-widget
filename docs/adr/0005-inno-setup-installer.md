# Inno Setup installer with our own update check, not Velopack

Task Widget ships as one Inno Setup 7 installer per architecture (x64, ARM64): per-user, no admin rights, Windows 11 only. Updates come from a check we write ourselves: once a day it asks GitHub Releases for the latest version, and the user clicks Update to download and run the new installer. Velopack would give us delta updates and a ready-made updater, but its package is ~25 MB against our 20 MB target (Inno's solid LZMA2 lands around 16–18 MB). Its uninstall also deletes `%LocalAppData%\{packId}` without asking, so it would either wipe the user's Tasks or force the data folder in ADR 0004 to move. Neither option keeps an updater process running, so RAM doesn't decide this; size and data safety do.

## Consequences

- Every update downloads the full installer (~17 MB), since there are no deltas.
- About 150 lines of update code are ours to write and maintain: a GitHub Releases query, version compare, download, then a silent installer run and restart.
- Uninstall can ask before deleting `%LOCALAPPDATA%\TaskWidget\`; it keeps the data by default and when run silently.
- Moving to Velopack later means a one-time handover (an Inno update that installs the Velopack build) and moving the data folder out of Velopack's install root.
