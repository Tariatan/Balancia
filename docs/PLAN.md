# Implementation plan and checkpoint

Last updated: 2026-09-17.

## Milestones

| Milestone | Deliverable | Exit evidence | Status |
| --- | --- | --- | --- |
| M0 | Specification and context harness | Files, working local links, harness check, source unchanged | Complete |
| M1 | Pinned SDK/packages, solution, Windows shell, test projects | Restore/build/test and manual shell launch; exact commands in README | Complete |
| M2 | Accounts, categories, openings, ledger operations | L01–L07, SQLite atomicity tests, immediate UI refresh | Not started |
| M3 | Buxfer import preview and application | I01–I07, current local source reconciliation, repeated import no-op | Not started |
| M4 | Home, transaction form, history/search/filtering | H01–H05, keyboard/scroll checks, P01 measurement | Not started |
| M5 | Recurring templates and reminders | R01–R10, editable schedule and overdue behavior in UI | Not started |
| M6 | Backups and snapshot export | S01, S06; recovery tested before relying on migration | Not started |
| M7 | Android viewer and snapshot import | S02–S05 on device/emulator, packaging and refresh instructions | Not started |

Deliver working slices; do not build all infrastructure before displaying useful
data. M2 can start with a minimal editor, refined in M4. Windows usefulness precedes
Android polish. Keep the source CSV untouched during implementation.

## Current checkpoint

- Created AGENTS.md, MEMORY.md, README.md, specification, architecture, acceptance
  scenarios, this plan, ignore/editor defaults, and a PowerShell harness checker.
- Re-read current CSV type counts and SHA-256; former Refund is now Income.
- M1: pinned SDK 10.0.201, Avalonia 12.1.2 and other dependencies listed in README.
  Added Balancia.slnx with Core, Storage, Desktop and two test projects; committed
  dependencies will include five package lock files. Android tooling unchecked.
- Verified on 2026-09-17: `powershell -NoProfile -File scripts/Check-Harness.ps1`
  passed required-file, local Markdown link, and private-data ignore checks.
- Verified on 2026-09-17: the same command with `-CheckPrivateImport` passed and
  inspected 808 rows without printing financial values. Source SHA-256 remained
  unchanged from the start of this work.
- Git initialized on main; private GitHub repository created at
  https://github.com/Tariatan/Balancia on 2026-09-17. Private CSV remains ignored.
- Verified M1: locked restore passed; Release build passed with zero warnings and
  zero errors; 11 tests passed (8 Core, 3 Storage). Commands are in README.
- Core tests cover exact centime conversion, signed boundaries, fractional-centime
  rejection and checked overflow. Storage tests use real temporary SQLite files
  for reopen persistence and per-connection foreign-key enforcement. These are
  foundational checks, not full L01–L07 ledger acceptance.
- Launched the Windows shell using the documented dotnet run command. Exercised
  Overview, Accounts, Transactions, Recurring payments and return to Overview.
  Inspected screenshots and accessibility text; fixed sidebar normal/hover contrast
  and rebuilt/relaunched successfully. The preview remains open for review.
- No real financial data is loaded; no live database/schema, importer, editable
  ledger, Android build, or CI exists. M1 changes are local, not yet committed/pushed.

## Next concrete action

Begin M2: schema/migrations, account/category management, opening balances, atomic
income/expense/transfer operations, and a minimal editor. Implement L01–L07 with
real SQLite integration tests; update the UI from committed reads. Close the
running preview before rebuilding on Windows.

## Handoff format for subsequent work

Record date, milestone, behavior implemented, files affected, exact checks and
results, limitations, and next action. Update existing checkpoint facts rather than
appending a full transcript. Keep unfinished milestones visibly unfinished.
