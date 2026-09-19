# Implementation plan and checkpoint

Last updated: 2026-09-19.

## Milestones

| Milestone | Deliverable | Exit evidence | Status |
| --- | --- | --- | --- |
| M0 | Specification and context harness | Files, working local links, harness check, source unchanged | Complete |
| M1 | Pinned SDK/packages, solution, Windows shell, test projects | Restore/build/test and manual shell launch; exact commands in README | Complete |
| M2 | Accounts, categories, openings, ledger operations | L01–L07, SQLite atomicity tests, immediate UI refresh | Complete |
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
- M1 scaffold was committed and pushed as `310d671` on main.
- M2 adds SQLite schema v1; account/category create, edit, archive; editable opening
  balances; atomic income, expense, and transfer create/edit/delete; dashboard
  balances, current-month flows, and largest categories from committed data.
- M2 verification on 2026-09-19: locked restore, Release build with zero warnings,
  all 23 tests passed (8 Core, 15 Storage). Integration tests cover L01–L07,
  rollback on injected transfer create/edit/delete failure, archive rules, and
  an unsupported schema version. `scripts/Check-Harness.ps1` passed.
- Windows interaction: in a separate temporary dataset, added a synthetic account
  and expense, confirmed immediate values, closed/reopened the app and observed
  CHF 980 net worth from CHF 1,000 opening minus CHF 20 expense. Editing that
  expense to CHF 25 updated the transaction row. Empty amount validation kept the
  form open. The default user database and private CSV were not used.
- M2 changes are ready to commit and push. CSV import, reminder matching, snapshots,
  Android, and high-volume history tuning remain in later milestones.

## Next concrete action

Begin M3: implement Buxfer CSV preview and idempotent, atomic application. Re-read
the private source only for local reconciliation; preserve its original bytes.
Close the running separate-data preview before rebuilding on Windows.

## Handoff format for subsequent work

Record date, milestone, behavior implemented, files affected, exact checks and
results, limitations, and next action. Update existing checkpoint facts rather than
appending a full transcript. Keep unfinished milestones visibly unfinished.
