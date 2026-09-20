# Implementation plan and checkpoint

Last updated: 2026-09-20.

## Milestones

| Milestone | Deliverable | Exit evidence | Status |
| --- | --- | --- | --- |
| M0 | Specification and context harness | Files, working local links, harness check, source unchanged | Complete |
| M1 | Pinned SDK/packages, solution, Windows shell, test projects | Restore/build/test and manual shell launch; exact commands in README | Complete |
| M2 | Accounts, categories, openings, ledger operations | L01–L07, SQLite atomicity tests, immediate UI refresh | Complete |
| M3 | CSV import preview and application | I01–I07, current local source reconciliation, repeated import no-op | Complete |
| M4 | Home, transaction form, history/search/filtering | H01–H05 history/dashboard checks, keyboard/scroll checks, P01 measurement | Complete |
| M5 | Recurring templates and reminders | R01–R10, editable schedule and overdue behavior in UI | Complete |
| M6 | Backups and snapshot export | S01, S06; recovery tested before relying on migration | Complete |
| M7 | Android viewer and snapshot import | S02–S05 on device/emulator, packaging and refresh instructions | Complete |
| M8 | Release polish and distribution | Release APK, backup UX, documentation, regression checks | Next |

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
- M3 adds schema v2 import provenance, backup before v1 migration, strict CSV
  CSV preview, Windows file selection and apply dialog, atomic import, reimport
  conflict detection, and account reconciliation. The source CSV remains ignored.
- Verified 2026-09-19: locked restore and Release build passed with zero warnings;
  33 tests passed (8 Core, 25 Storage), including an opt-in isolated import of
  the current private export and per-account reconciliation. Synthetic tests cover
  I01–I05/I07 and backup migration. Unchanged reimport adds no entries and does
  not increment revision. No real export was applied to the normal app database.
- Windows interaction checked in a separate synthetic dataset: Transactions
  exposes Import CSV and opens the CSV picker. The preview/apply modal was
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
- M5 editor slice is now implemented locally: the Recurring payments page can add
  and edit templates, including expected-date changes used for manual rescheduling,
  indicative amount, interval, and archive state. Active duplicate descriptions
  are rejected case-insensitively. Sequential Release verification passes 8 Core
  and 35 Storage tests; the earlier parallel test/build collision was an output
  file race, not a code failure.
- M5 editor was committed and pushed as `ce2c14e`.
- M6 started locally with a consistent snapshot exporter. It uses SQLite backup
  into a temporary database, packages `ledger.db` plus a manifest containing
  dataset, schema, revision, and export time, then atomically replaces the target
  artifact. Synthetic extraction/manifest validation passes; snapshot import,
  recovery promotion, and desktop export UI remain next.
- Snapshot validation is now implemented locally: archive format, manifest/database
  identity, schema, revision, and SQLite integrity are checked from a temporary
  extracted copy. Invalid archives are rejected without touching the ledger. The
  synthetic suite now has 37 passing tests; import promotion and UI export remain.
- Recovery promotion is now implemented locally. A validated snapshot must match
  the current dataset and cannot have an older revision; promotion creates a
  timestamped `.pre-restore-*.bak` first, then replaces the database from a staged
  copy. The older-snapshot recovery test passes. Windows export/restore UI remains.
- Recovery promotion was committed and pushed as `38c8cd1`. The Overview now has
  Export snapshot and Restore snapshot actions using the native file pickers;
  restore validates first and reports the generated backup. Build and 38 tests
  pass after the UI change. A manual picker/restore interaction remains to verify.
- Snapshot error reporting was committed and pushed as `6b91899`; approved manual
  behavior confirms restore is same-dataset recovery, not cross-dataset import.
- M7 started on 2026-09-19 after the Android workload became available. A native
  `Balancia.Android` app targets `net10.0-android` / API 36, references Core and
  Storage, and now provides document selection, app-private staging, shared
  validation, embedded database promotion, and a read-only net-worth/monthly
  summary. Debug build passes and Release APK artifacts are produced. Samsung
  S20 FE installation and transaction search remain.
- The custom-extension picker fix was committed and pushed as `0dbbf6a`. Android
  description search is now implemented over the staged snapshot and the Debug
  build passes. Device installation and on-device search verification remain.

## Next concrete action

Begin M8: package and document the Android release workflow, improve snapshot
refresh/error presentation, and run a final Windows/Android regression pass.

## Overview design refinement — 2026-09-19

The selected horizontal-navigation overview is implemented locally. Net worth
lists each account and a total; history is the wide left panel, with categories
and reminders stacked to the right. The period controls offer All, This Week,
This Month, This Year, and inclusive Custom dates. The period changes income,
expenses, categories, and visible history while balances/reminders stay current.
Visible currency labels were removed from Windows and Android. The data model
remains CHF-only. Locked restore and Release solution build passed with zero
warnings/errors; 47 tests passed, including a new synthetic range test. A Windows
synthetic-data interaction checked the layout and period choices. This work is
local and uncommitted; M8 release packaging remains the next milestone.

The owner next requested a 1280 × 1024 minimum window, all period-matching
transactions in Overview, and a Transactions page matching the overview style.
These changes are implemented locally. A synthetic query with 1,005 matches
verified no overview cap; the Windows UI showed 221 overview matches and scrolled
to the oldest row, while Transactions retained its search and edit flow. The
minimum size was checked by attempting a smaller window resize. The Release
solution build passed with zero warnings/errors and 48 tests passed. The owner's
already-running Debug app was left open; the separate Release test window was
closed. M8 remains next.

The 2026-09-19 follow-up raises the minimum to 1280 × 1280, enlarges and colors
history amounts by kind, anchors the overview history/actions and Transactions
history to the resizable body, and selects an overview double-clicked record on
the correct Transactions page. The Description input ends beside Max amount.
The Transactions toolbar offers Import CSV and Export CSV. Export writes a
consistent nine-column ledger file; the import code and visible copy use generic
CSV names while matching prior provenance labels by external ID. Synthetic
Windows UI checks confirmed resize anchoring, row navigation, aligned input
edges, no outer Transactions scrollbar, and the Export CSV picker. Locked restore
and Release solution build passed with zero warnings/errors; 50 tests passed
(8 Core, 42 Storage), including export quoting/transfer and prior-provenance
reimport checks. These changes remain local and uncommitted; M8 remains next.

The next 2026-09-19 UI refinement removes Overview's Add account and snapshot
buttons. Import/Export CSV and Export/Restore snapshot now sit together in the
Transactions header. Navigation orders Transactions and Recurring payments before
Accounts and Categories. The Custom period Apply button aligns with its date
inputs. Overview income, expense, and net-worth values have sign-based colors;
history gives Category more space by narrowing Account. A shared translucent
selection style improves list-row readability. Categories has top Add/Edit
actions and a list that fills the remaining page. Synthetic Windows checks
confirmed the selected-row contrast, action locations, date-input alignment,
and a fixed Categories bottom gap while resizing. Locked restore and final
Release solution build passed with zero warnings/errors; all 50 tests passed.
The final translucent selection rendered as RGB 225/237/242 against white on a
selected synthetic row. The separate Release test app was closed. M8 remains next.

The next local Overview adjustment gives the compact Category column 170 layout
units and sizes Account to 118, enough for the representative transfer label and
its cell padding. The lower panel split changes from 1.5:0.85 to 1.7:0.85 so
Account visibly moves right while Category grows and Amount keeps its width.
Description remains flexible. A Release Desktop build passed with zero
warnings/errors; a synthetic Overview UI check showed the Account header 62
screen pixels farther right than the first adjustment, with the Category
column 25 layout units wider than the prior version.

## Handoff format for subsequent work

On 2026-09-20, `MainWindow.axaml.cs` was split into focused partial-class files
for overview, transactions, categories, recurring payments, accounts, data
transfer, and shared UI helpers. This refactor changes source organization only.
Locked restore and Release solution build passed with zero warnings and errors;
all 50 tests passed (8 Core, 42 Storage). Manual UI interaction was not run for
this source-only refactor.

The 2026-09-20 follow-up removes the dedicated Recurring payments navigation and
page. Upcoming payments on Overview now owns a selectable reminder list with Add
recurring template and Edit selected actions; the Calendar link and tab are gone.
Recurring reminder storage and calculation remain unchanged.

The following UI pass restores the compact Upcoming payments rows, adds + and
dustbin header actions, makes rows double-click editable, and removes Archive
from the recurring edit form. Desktop build and all tests passed; synthetic UI
inspection found only the header, +, and dustbin actions in the card.

Record date, milestone, behavior implemented, files affected, exact checks and
results, limitations, and next action. Update existing checkpoint facts rather than
appending a full transcript. Keep unfinished milestones visibly unfinished.

The 2026-09-20 Windows usability follow-up persists the desktop width, height, and
position to an atomic `window.json` file beside the selected data folder. It loads
valid settings before startup and reapplies the saved position after the window is
opened; malformed settings are ignored. Release Desktop build, all 50 tests, and a
synthetic launch/close/relaunch check with seeded geometry passed. The CUA surface
was unavailable for a direct resize gesture, so the geometry check used the real
desktop executable and its close/restart lifecycle.

The next Windows form refinement replaces the transaction date text box with the
shared filter `DatePicker`, defaulting new transactions to Today, and evaluates
basic amount expressions on focus leave and save. Release Desktop build and the
existing test suite passed; direct form interaction remains to be checked in the
Windows UI.
## 2026-09-20 UI checkpoint

The Transactions tab was removed. CSV and snapshot actions now appear at the
top of Settings; Overview remains the transaction editing surface.

Overview transaction history now provides add and guarded delete header actions;
double-clicking a row opens its edit form directly.

The standalone Accounts tab was removed. Overview now provides an Accounts
card with selectable rows, add and guarded delete actions, and double-click
editing. Accounts containing transaction history cannot be deleted and remain
archivable through the edit form. The desktop navigation is Overview,
Transactions, and Categories.
