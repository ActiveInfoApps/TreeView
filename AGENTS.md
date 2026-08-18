# AGENTS.md

## Change Log Rule

After every process that modifies the code append an entry to:

  "C:\TreeView\Documents\Change List.md"

Format:
- Entries are numbered serially (1, 2, 3, …), continuing from the last entry in the file.
- Each entry starts with its number, followed by a timestamp (YYYY-MM-DD HH:MM UTC), followed by a short plain-English description of what changed and why.
- Read the file first to find the current highest number before appending.

Example entry:

  12. 2026-07-30 14:22 UTC — Added 3-tier price resolution to kiosk characterization and web app spec. Kiosk price list takes priority, then store price list, then baseline.

Write one entry per user request (not one per file edited). If multiple files were changed as part of a single request, cover all of them in one entry.

## Solution structure

```
DiskSpaceTree.sln
├── DiskSpaceTree.Core/       net9.0, zero NuGet deps — scanner engine, models, diagnostics
├── DiskSpaceTree.Data/       net9.0 — EF Core SQLite persistence layer
├── DiskSpaceTree.WinForms/   net9.0-windows — WinForms UI (main app)
├── DiskSpaceTree/            net9.0 MAUI — cross-platform shell (references Core only)
└── DiskSpaceTree.Tests/      net9.0 — xunit tests (references Core only)
```

## Build & test commands

```bash
dotnet build DiskSpaceTree.sln
dotnet test DiskSpaceTree.Tests/DiskSpaceTree.Tests.csproj
```

No linting, formatting, or typecheck commands are configured.

## Testing

- Framework: xunit 2.9.2
- Tests reference `DiskSpaceTree.Core` only (not Data or WinForms)
- `InMemoryFileSystemAccessor` implements `IFileSystemAccessor` for in-memory scan testing
- `TreeBuildBench.Stress_TreeBuild_ReportsEventCounts` subscribes AFTER the scan — counters (reset/add/remove) will be zero. It only asserts elapsed time < 10s. Do not treat it as a regression guard for event counts.

## EF Core migrations

The Data project uses EF Core 9.x with SQLite.

- DB file: `C:\TreeView\Database\scanhistory.db` (created automatically)
- Migrations run on app startup via `Program.cs` → `context.Database.MigrateAsync()`
- The persistence service also calls `MigrateAsync()` before saving (safe double-call)
- To add a new migration: `dotnet ef migrations add <Name> --project DiskSpaceTree.Data --startup-project DiskSpaceTree.WinForms`
- WinForms is the startup project for EF tooling (it has the Design package reference)
- Entity `DiskSpaceTree.Data.Entities.Directory` collides with `System.IO.Directory` — always use `Entities.Directory` in Data project files

## Architecture

### Scanner (DiskSpaceTree.Core)

Two-stage pipeline in `DiskSpaceScanner.ScanDirectoryAsync`:
1. **ListingDirectories** — `BuildDirectoryListAsync` discovers dirs, builds `FileSystemNode` tree, enqueues each dir
2. **ScanningFiles** — `DrainDirectoryQueueAsync` processes queue (FIFO), calls `AccumulateToSelfAndAncestors` for size rollup, fires `DirectoryCompleted` per dir, triggers `SortChildrenBySizeDescending` (via `ObservableChildCollection.ReplaceWith`) when a parent's last child finishes

Key constants: `DefaultMaxDirectoriesToScan = 10000`, `DirectoryScanTaskDepth = 4`

The `IFileSystemAccessor` abstraction enables testing without real filesystem access.

### Tree view (WinForms)

- Root node subscribed to `Children.CollectionChanged` at scan start
- `SubscribeToNode` is idempotent (checks `_nodeMap`) — safe to call repeatedly
- `SortChildrenBySizeDescending` uses `ReplaceWith` (single suppressed Reset) — no per-item event flood
- Reset handler wraps rebuild in `BeginUpdate/EndUpdate` to batch layout
- Listing-phase Children events are ignored (`_scanner.CurrentStage == ListingDirectories`)

### Persistence (DiskSpaceTree.Data)

- Three tables: `Execution` (scan run), `Directory` (master path list, self-referencing parent FK), `DirectoryInfo` (per-execution snapshot)
- `ScanPersistenceService.SaveScanResultsAsync` walks the `FileSystemNode` tree, upserts directories by path, creates info snapshots, marks missing directories as deleted
- Fire-and-forget from MainForm's `finally` block after scan completes

## Gotchas

- `ObservableChildCollection.ReplaceWith` suppresses per-item events, raises a single `Reset` — do not call `Clear()` + `Add()` in a loop for batch reordering
- `FileSystemNode.SyncRoot` is the lock for thread-safe property updates — respect it when accessing node properties from background threads
- The `Database/` directory at repo root is for the SQLite DB, not build output
