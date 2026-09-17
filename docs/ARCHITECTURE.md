# Architecture and decision record

2026-09-17. This is a design, not an inventory of implemented components.

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

Start by verifying/pinning a .NET SDK and compatible Avalonia version. SDK 10.0.201
is present, but compatibility and Android workloads are unverified. Data-access
library and test framework selection remain implementation choices. Record exact
versions in project files and build commands in README once verified.

## Proposed storage model

- Account: stable ID, name, CHF currency, archive state.
- Category: stable ID, name, optional parent; at most two levels.
- LedgerTransaction: ID, kind, date, description, optional category, optional memo.
  Kinds include an internal OpeningBalance distinct from the three entry-form types.
- Movement: transaction ID, account ID, signed Int64 centimes. Expense has one
  negative movement; Income one positive; Transfer two summing to zero; Opening
  one signed movement. Opening entries have an effective date.
- RecurringTemplate: description, indicative centimes, intervalMonths, desired
  day, active schedule anchor and manual-reschedule boundary, archive state.
- ImportSource: external system/ID, normalized source fingerprint and batch ID,
  linked logical transaction; retain both source rows for transfer provenance.
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

- Exact SDK, Avalonia, SQLite provider, MVVM utilities, and test framework versions.
- Windows distribution/update method and Android sideload packaging.
- Snapshot encryption, backup retention, and full restore UX.
- Android document-provider compatibility and whether automatic export adds value.
- Measured performance against the proposed 50,000-row acceptance dataset.
