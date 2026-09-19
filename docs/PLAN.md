# Implementation plan and checkpoint

Last updated: 2026-09-19.

## Milestones

| Milestone | Deliverable | Exit evidence | Status |
| --- | --- | --- | --- |
| M0 | Specification and context harness | Files, working local links, harness check, source unchanged | Complete |
| M1 | Pinned SDK/packages, solution, Windows shell, test projects | Restore/build/test and manual shell launch; exact commands in README | Complete |
| M2 | Accounts, categories, openings, ledger operations | L01–L07, SQLite atomicity tests, immediate UI refresh | Complete |
| M3 | Buxfer import preview and application | I01–I07, current local source reconciliation, repeated import no-op | Complete |
| M4 | Home, transaction form, history/search/filtering | H01–H05 history/dashboard checks, keyboard/scroll checks, P01 measurement | Complete |
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
- M2 committed and pushed as `576db1f` on main.
- M3 adds schema v2 import provenance, backup before v1 migration, strict Buxfer
  CSV preview, Windows file selection and apply dialog, atomic import, reimport
  conflict detection, and account reconciliation. The source CSV remains ignored.
- Verified 2026-09-19: locked restore and Release build passed with zero warnings;
  33 tests passed (8 Core, 25 Storage), including an opt-in isolated import of
  the current private export and per-account reconciliation. Synthetic tests cover
  I01–I05/I07 and backup migration. Unchanged reimport adds no entries and does
  not increment revision. No real export was applied to the normal app database.
- Windows interaction checked in a separate synthetic dataset: Transactions
  exposes Import Buxfer CSV and opens the CSV picker. The preview/apply modal was
  not exercised through UI automation; its application path is covered by SQLite
  integration tests. M4 can refine long history and preview presentation.
- Owner verified the M3 import in the Windows app on 2026-09-19: account balances
  calculated precisely, and accounts, transactions, and categories imported and
  appeared in the expected order. This confirms the real preview/apply flow beyond
  the isolated integration test. No private financial values are recorded here.
- M3 was committed and pushed as `ea77985` on main.
- M4 separates dashboard aggregates from history rows. The overview loads five
  recent entries; Transactions loads 100 per page with stable cursor paging,
  description search, account/type/category/date/amount filters, and a category
  chooser narrowed by typed text. Parent-category filtering includes children.
- Synthetic H01–H05 storage checks passed for combined and inclusive filters,
  Unicode case-insensitive search, category hierarchy, stable paging, transfer
  effects, immediate edit totals, and a simulated month change. Recurring reminder
  display remains M5; H05's dashboard month behavior is covered here.
- Windows interaction on synthetic data: searched `LuNcH` (110 matches), paged to
  entries 101–110, combined a minimum amount filter (88 matches), narrowed the
  category chooser, edited an expense and saw the row and overview totals update.
  A final relaunch navigated Overview → Transactions → Overview successfully.
- P01 Release measurement on 50,000 synthetic transactions, 30 warm samples each
  on an Intel Core i9-14900K Windows PC: p95 first page 68.8 ms, deep cursor page
  50.9 ms, search 49.6 ms, combined filter 86.3 ms, dashboard 33.9 ms, and
  committed edit plus dashboard read 104.2 ms. These are storage/query timings,
  not end-to-end Avalonia render timings.
- Final M4 verification on 2026-09-19: locked restore; Release build with zero
  warnings/errors; 39 tests passed (8 Core, 31 Storage); harness passed; private
  CSV remains Git-ignored. M4 changes are local and uncommitted.
- M5 started locally: schema version 3 adds recurring templates with description,
  expected date, indicative amount, interval months, and archive state. Reminder
  calculation matches exact description and occurrence month/year, ignores amount
  and day, keeps overdue occurrences visible, advances only through satisfied
  scheduled months, and preserves the original desired day across month-end clamps.
  Storage tests cover monthly satisfaction, earlier-payment rejection, and the
  January-31/February/ March cadence. Overview and the Recurring payments page
  now display reminders; template editing and manual rescheduling remain next.

## Next concrete action

Continue M5: add the full recurring-template editor, duplicate-description
handling, manual rescheduling boundary, and UI checks for overdue/paid states.

## Handoff format for subsequent work

Record date, milestone, behavior implemented, files affected, exact checks and
results, limitations, and next action. Update existing checkpoint facts rather than
appending a full transcript. Keep unfinished milestones visibly unfinished.
