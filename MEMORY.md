# Balancia project memory

Last updated: 2026-09-19. Scope: this repository only.

## Durable context

- Owner: Slava. Personal tool and learning project; enjoys C#.
- Prior dashboard inspiration: net worth, calendar-month income/expenses, largest expense
  categories, upcoming payments, then transaction history.
- Windows performs all writes. Android checks balances and searches history;
  stale snapshots and manual refresh are acceptable. Offline local operation.
- Accounts: UBS, Cash, Revolut; CHF only. Opening balances and transfers matter.
- Expense, Income, Transfer; description plus optional category/subcategory.
  No split transactions, bank sync, budgeting limits, or paid hosting.
- Recurrence: exact description within scheduled month/year, ignoring amount/day;
  every N months; editable expected date; unpaid occurrences remain overdue.
- User corrected the former Refund entry to Income. Do not implement Refund as a
  product type. Unknown future import types need an explicit import error/mapping.
- Agreed stack: C#/.NET, Avalonia, SQLite; one-way Google Drive snapshot transport.
  Windows dependencies are pinned and tested; Android transport remains unverified.

## Current evidence

- Initially the directory contained only transactions.csv; no ancestor AGENTS.md
  was found along the workspace path.
- Git initialized on main on 2026-09-17. Private GitHub repository created at
  https://github.com/Tariatan/Balancia. The source CSV remains ignored.
- Refreshed CSV inspection, 2026-09-17: 808 rows; Expense 735, Income 14,
  Transfer 59; no blank Type. SHA-256:
  `FE5C3C2370AD2C22B0E2550BC2563B8AD58FDD665485D83B9D3F2F3894C3951E`.
- Earlier inspection found 28 two-row transfer pairs sharing CSV IDs and three
  opening-balance rows. Treat that structural result as prior evidence until the
  importer revalidates it against the current file; do not blindly trust counts.
- M1 completed and pushed at `310d671`: SDK 10.0.201/net10.0, Avalonia 12.1.2, Microsoft.Data.Sqlite 10.0.12,
  xUnit 2.9.3, VS runner 4.0.0 and Test SDK 18.10.1 pinned, with package lock files.
- Locked restore and Release build passed (zero warnings/errors); 11 tests passed.
  Windows shell launched and navigation/visual contrast checked. No CSV was loaded
  into the app and no live database was created. Source CSV hash is unchanged.
- M2 completed on 2026-09-19: SQLite schema v1, account/category/opening
  management, atomic income/expense/transfer operations, real Windows forms, live
  balances and monthly totals. All 23 tests pass; Release build zero warnings.
  Synthetic UI data persisted across restart and an edit refreshed immediately.
  Actual CSV CSV remains outside the app.
- M2 committed and pushed as `576db1f` on main. M3 was committed and pushed as
  `ea77985` on 2026-09-19: schema v2 provenance plus pre-migration backup, CSV preview/apply,
  atomic imports, no-op repeat, and explicit changed-ID/local-edit conflicts.
  Locked restore/Release build pass; 33 tests pass. Current private CSV was
  reconciled account by account only in an isolated test database. Windows UI
  picker opened in a synthetic dataset; preview/apply UI remains unexercised.
- Owner verified the live M3 import on 2026-09-19: precise account balances and
  correctly imported/ordered accounts, transactions, and categories. No private
  values are retained in repository memory.
- M4 is complete locally on 2026-09-19, not yet committed: split dashboard/history
  reads, 100-row cursor pages, combined filters, Unicode case-insensitive search,
  category chooser search, and immediate dashboard refresh after edits. Synthetic
  UI search/paging/filter/edit/navigation checks passed. Release storage p95 on
  50,000 synthetic rows was at most 104.2 ms across measured paths; UI rendering
  latency was not measured. Recurring reminder display remains M5.
- M4 was committed and pushed as `48a4b7e`. M5 is local and uncommitted: schema
  version 3 recurring templates and reminder calculation are implemented, with
  34 storage/core tests passing and the desktop overview showing reminder rows.
  Full template editing, duplicate-description UX, and manual rescheduling are
  still outstanding.

## Resume here

M5, M6, and M7 are now verified complete. M7 was tested on the Samsung S20 FE:
snapshot selection, validation, refresh, offline retained snapshot, and Android
description search all work. See docs/PLAN.md for the M8 release-polish checkpoint.

On 2026-09-19, the owner selected a horizontal-navigation overview design with
account rows in net worth, a wide left history, stacked categories/reminders,
and All/This Week/This Month/This Year/Custom period choices. This is implemented
locally and uncommitted. Synthetic Windows UI interaction and 47 tests passed;
Android currency-label removal was built but not checked on device. Visible
amounts omit CHF labels, while storage remains CHF-only. M8 packaging remains next.

The next 2026-09-19 local refinement sets the Windows minimum to 1280 × 1024,
loads all overview history matching its period into a virtualized list, and
restyles Transactions with the shared table and white card surfaces while
retaining its filters, paging, and edit/remove actions. A synthetic 1,005-row
range test, 48 total tests, and Windows UI checks passed; changes remain
uncommitted. The owner's Debug instance was not closed.

The following local refinement raises the minimum to 1280 × 1280, anchors the
overview history and bottom actions, and gives Transactions a height-filling
history without an outer page scrollbar. History text is 2 points larger,
amounts are bold and colored by transaction kind, and overview double-click
selects the same record on Transactions. CSV import labels are generic; CSV
export writes a distinct nine-column ledger file. Existing import provenance
labels remain readable by external ID. Synthetic UI navigation, resize, input
alignment, scroll, and Export CSV picker checks passed. Locked restore and Release
solution build succeeded with zero warnings/errors; 50 tests passed (8 Core,
42 Storage). The change remains local and uncommitted.

The subsequent local UI polish removes Overview's bottom actions, groups
CSV/snapshot actions on Transactions, reorders navigation, aligns Custom Apply,
uses green/red Overview money values, widens the history Category column, and
adds a translucent shared selected-row style. Categories now fills its page
below top Add/Edit actions. Synthetic Windows checks confirmed the layout,
selection contrast, and Categories resize behavior. Locked restore, Release
solution build, and all 50 tests passed after the final style change; see
docs/PLAN.md. No private ledger data was used for those checks.

The latest local Overview refinement widens the compact Category column to 170
layout units and fits the representative transfer account label in a 118-unit
Account column. The left history panel grows relative to the right cards, moving
Account visibly right while Amount retains its width. A synthetic Windows
Overview check and Release Desktop build passed; see docs/PLAN.md. The work is
uncommitted.

## Context maintenance

On 2026-09-20 the dedicated Recurring payments tab was removed. Overview's
Upcoming payments card now contains the selectable reminder list and Add/Edit
actions; Calendar navigation was removed. Reminder storage and calculation stay
unchanged.

The next Overview pass restored the compact reminder row look, moved Add to a +
header action, added a confirmed dustbin delete action, made row double-click open
the editor, and removed Archive from that editor. Build/tests and synthetic UI
inspection passed.

On 2026-09-20, the desktop `MainWindow` was split into partial files by feature,
with CSV/snapshot actions and shared UI helpers in separate files. The same
window, state, and storage calls remain; see docs/ARCHITECTURE.md for the layout.
Locked restore, Release solution build, and all 50 tests passed. Manual UI
interaction was not run for this source-only refactor.

Product truth belongs in docs/SPECIFICATION.md; technical reasoning belongs in
docs/ARCHITECTURE.md; progress belongs in docs/PLAN.md. Keep this handoff compact.
The owner's 2026-09-20 C# formatting preferences from CategoriesPanel and
RemindersPanel are recorded in docs/Coding Guidelines.md.
Label assumptions, timestamp evidence, and replace stale facts rather than append
contradictory entries. Do not copy the user's transaction details into this file.
