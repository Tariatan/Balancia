# Architecture and decision record

Updated 2026-09-19. This records accepted design and implemented slices.

## Accepted direction

| ID | Decision | Rationale and consequence |
| --- | --- | --- |
| ADR-001 | Local Windows writer, read-only Android | Matches actual usage; avoids server operations and multi-writer conflict resolution |
| ADR-002 | C#/.NET, Avalonia, SQLite | Shared language and domain logic; desktop-first UI; local relational persistence |
| ADR-003 | One logical transfer, two atomic movements | Preserves balances during all edits and excludes transfers from income/expenses |
| ADR-004 | Immutable snapshots via user-managed Drive transport | No hosted API; stale mobile data is acceptable; live database is never synced |
| ADR-005 | Description + occurrence month/year for recurrence | User-defined rule; no fuzzy matching, amount matching, or bank integration |

Change decisions here when evidence warrants it, noting date, reason, and what is
superseded. Product changes also belong in the specification. Do not add elaborate
ADR tooling before this table becomes difficult to maintain.

## Proposed code boundaries

```text
src/
  Balancia.Core/        Domain rules and application operations
  Balancia.Storage/     SQLite schema, migrations, queries, import/snapshot storage
  Balancia.Desktop/     Avalonia Windows views and view models
  Balancia.Android/     Read-only Avalonia Android shell (later)
tests/
  Balancia.Core.Tests/
  Balancia.Storage.Tests/
```

Core has no Avalonia, SQLite, file-picker, or cloud dependencies. Desktop invokes
application operations and reads query results. Storage implements persistence
boundaries required by those operations; avoid a generic repository for every
table. Android reuses read models/queries, not a writable desktop service surface.
Introduce shared UI only where reuse is demonstrated. A small solution is enough;
no microservices, HTTP API, event broker, or event-sourcing system is planned.

M1 pins SDK 10.0.201 / net10.0 with Avalonia 12.1.2, Microsoft.Data.Sqlite 10.0.12,
xUnit 2.9.3, VS runner 4.0.0, and Test SDK 18.10.1. Direct versions live in project
files; transitive versions are locked. Release build and tests validate this local
Windows combination. Android compatibility has not been exercised. No MVVM helper
package is needed for shell navigation; editable view models come with M2.

Sources checked during M1: [Avalonia Windows guidance](https://docs.avaloniaui.net/docs/platform-specific-guides/windows)
and [Avalonia NuGet package](https://www.nuget.org/packages/Avalonia.Desktop/12.1.2).
Package availability was also checked against the NuGet flat-container API.

M2 adds typed ledger drafts/read models, a version 1 SQLite schema, a storage
boundary with atomic writes, and editable Windows forms. Account balances are
recomputed from signed movements; writes validate balances and monthly totals
before commit. The schema rejects unsupported versions without changing them.
All current queries rebuild the small snapshot; paging/query tuning belongs to
M4. No MVVM utility package was needed for this initial editor; revisit that
choice when form complexity grows.

M3 upgrades to schema version 2. A consistent SQLite backup is written beside
the database before a version 1 migration. `import_sources` links each CSV ID
to one logical ledger entry, stores both raw transfer rows and a canonical source
fingerprint, and records local edits/deletions as conflicts for reimport. CSV
preview validates all rows and groups before the existing atomic write boundary.
Apply rechecks the file hash and ledger revision, then reconciles account movement
totals. An unchanged full reimport leaves the revision unchanged. The import UI
uses the Windows file picker and an explicit preview/apply dialog.

M4 adds separate desktop aggregate and history queries. The overview reads
account/month totals and five recent entries; history uses stable date/ID cursor
pages of 100 instead of loading every row. Parameterized SQL combines filters;
an SQLite connection function delegates substring comparison to .NET ordinal
case-insensitive matching so accented case pairs work consistently. A count query
supports result totals. The write path checks movement and month totals without
materializing every history entry. On 50,000 synthetic rows, Release storage/query
p95 ranged from 33.9 to 104.2 ms for measured reads and edit-plus-refresh;
Avalonia rendering time was not included. These measurements do not justify a
balance cache or full-text index yet.

The 2026-09-19 overview refinement reads aggregates for an optional inclusive
date range and applies the same range to its history. A null range means
all dates. Account balances still sum all posted movements, and reminder queries
retain their independent next-occurrence semantics. The default monthly read
method remains available for existing consumers.
The overview now reads all matching history in one SQLite read transaction and
shows it through a height-constrained, virtualized Avalonia ListBox. The overview
history filter panel shares the row layout with the history card; there is no
separate Transactions page. Double-clicking a row opens the transaction editor
directly. CSV export reads one snapshot, writes a temporary file with standard
quoting, then replaces the selected target after the write completes. Import
provenance queries match external IDs across existing source labels so records
imported before the UI rename remain idempotent.
Overview filter and period changes now keep the existing Avalonia layout mounted.
The desktop updates metric text, category rows, history items, and paging state
in place, skipping list replacement when visible hits are unchanged. Opening or
closing the filter changes only that panel. Filter reads do not disable the
page, and changes made during a read queue one refresh for the latest filter.
The next Windows layout refinement uses a shared application-level ListBoxItem
template-presenter style so focused and unfocused selections retain the same
light, readable color across pages. Categories joins the height-filling content
host; its list owns its scrolling. Snapshot actions move to the Transactions
header, and the overview's history occupies the space to the page bottom.

The desktop persists window geometry separately from ledger data. `window.json`
is written beside the active `balancia.db` with an atomic replace on close and
contains width, height, and screen position. Invalid settings are ignored so a
damaged optional UI preference cannot block ledger startup.
The selected database file is stored independently in `%LOCALAPPDATA%\Balancia\settings.json`.
Settings can switch the active folder at runtime; the replacement store is
initialized before it is assigned, and the selected path is written atomically.
The same settings file optionally stores a backup folder. The desktop close
handler exports a consistent snapshot with a unique timestamped filename, then
retains only the ten newest backup files. Backup failures are swallowed during
shutdown so they cannot prevent the application from closing.
An independent snapshot folder is persisted alongside the backup folder. Each
clean close exports `Snapshot.balancia` there without retention pruning, so
the folder can be synchronized manually to Google Drive and consumed by Android.
The transaction editor reuses the filter `DatePicker`; its amount input uses a
small decimal recursive-descent evaluator for the four basic operators and
normalizes valid results to centime precision before the existing `Money` parser.
Its category `AutoCompleteBox` suggests active category paths while preserving
typed text. Storage resolves case-insensitive path components inside the same
SQLite write transaction as the ledger entry; a failed save rolls back newly
created parent and child categories with the entry.
The desktop window handles `+` for quick transaction entry outside text controls.
The shared edit dialog accepts an optional successful-save callback for Add
transaction: its default Enter action saves and resets the entry fields without
closing, while its ordinary Save button commits and closes.

On 2026-09-20, the desktop window code was split into partial `MainWindow`
files by responsibility. `MainWindow.axaml.cs` owns state, initialization,
refresh, navigation, and page selection. `MainWindow.Overview.cs`,
`MainWindow.Transactions.cs`, `MainWindow.Categories.cs`,
`MainWindow.RecurringPayments.cs`, and `MainWindow.Accounts.cs` own feature UI;
`MainWindow.DataTransfer.cs` owns CSV and snapshot actions; and
`MainWindow.UiHelpers.cs` owns shared controls and dialogs. This is a source
organization change; the same window and storage boundary remain in use.

## Storage model

- Account: stable ID, name, CHF currency, archive state.
- Category: stable ID, name, optional parent; at most two levels.
- LedgerTransaction: ID, kind, date, description, optional category, optional memo.
  Kinds include an internal OpeningBalance distinct from the three entry-form types.
- Movement: transaction ID, account ID, signed Int64 centimes. Expense has one
  negative movement; Income one positive; Transfer two summing to zero; Opening
  one signed movement. Opening entries have an effective date.
- RecurringTemplate (planned): description, indicative centimes, intervalMonths, desired
  day, active schedule anchor and manual-reschedule boundary, archive state.
- ImportSource (implemented): external system/ID, normalized source fingerprint,
  linked logical transaction and raw source rows; retain both transfer rows.
- Metadata: schema version, dataset UUID, monotonic revision.

Use foreign keys, uniqueness/check constraints where appropriate, and a database
transaction for every logical write. Verify cross-row invariants before commit.
Do not independently persist mutable balance totals initially; compute them from
indexed movements. Cache only after measurement, with invalidation on every write.
Use DateOnly semantics for financial dates, UTC instants for snapshot metadata.

Candidate indexes: ledger date + stable ID; movement account + transaction;
category + date through query design; description/date for recurrence. Exact index
selection must follow actual query plans. A leading-wildcard substring search may
scan descriptions; benchmark before promising index acceleration or adding FTS.

## Consistency and lifecycle

Save operation: validate -> begin transaction -> write full operation -> increase
revision -> commit -> notify UI to reload affected reads. Failed operations roll
back and do not emit a successful revision. Inject a clock for month changes and
recurrence tests. Recompute reminder satisfaction after transaction changes.

Snapshot export uses SQLite's consistent backup facilities or a consistent logical
export, never a raw copy of an open WAL database. Proposed initial transport is a
single versioned archive containing a self-contained SQLite snapshot and manifest.
Hash the payload; verify schema/integrity and close all handles before promoting
an imported copy. Hashes detect corruption, not malicious authenticity.

Android copies the selected document into staging, validates it, and swaps its
app-local copy only on success. Interrupted transfer/import keeps the prior copy.
The Drive provider and device picker must be tested; app-internal Google OAuth is
not required by the accepted scope. Runtime databases and backups live outside the
repository even though ignore rules provide an additional accidental-commit guard.

## Harness design

AGENTS.md is the operational entry point and explicitly routes to MEMORY.md.
Specification, architecture, acceptance, and plan each own one kind of information.
Memory holds only a compact current handoff. Avoid duplicating complete rules
across files. The local check detects missing files/links and private-data ignore
regressions; application correctness belongs to future domain/integration tests.

Reference: [official AGENTS.md documentation](https://learn.chatgpt.com/docs/agent-configuration/agents-md).
No project-specific agent skill or MCP service is needed for the initial harness.

## Choices to resolve in their implementation milestones

- Windows distribution/update method and Android sideload packaging.
- Snapshot encryption, backup retention, and full restore UX.
- Android document-provider compatibility and whether automatic export adds value.
- End-to-end UI latency at 50,000 rows, beyond measured storage timings.

Desktop edit dialogs mark a successful final save before closing and refresh
the mounted page only in that case. Cancellation does not trigger a full
Overview rebuild, preventing visible redraws when dismissing delete dialogs.
