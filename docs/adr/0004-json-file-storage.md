# Tasks in one JSON file with a 90-day Done window, not SQLite

Tasks, Captures and the draft live in one `tasks.json` under `%LOCALAPPDATA%\TaskWidget\`, loaded whole into memory, written with System.Text.Json source generation. SQLite would keep only what's on screen in memory and make years of Done history free, but it adds a native DLL per architecture, engine memory, and NativeAOT setup for a data set that is small while it stays bounded. RAM comes first, so we bound it instead: Done Tasks older than 90 days, and Captures whose Tasks have all gone, move to an append-only `archive.jsonl` that the app never loads.

## Consequences

- The Done view shows only the last 90 days; older Done Tasks are kept on disk but can't be seen or restored from the Widget.
- Durability is ours to build: atomic replace with a `.bak`, a corrupt file renamed and never deleted, `schemaVersion` with forward migration, and a read-only Widget when the file comes from a newer version.
- Moving to SQLite later means a one-time import of `tasks.json` and `archive.jsonl`.
