# Balancia project memory

Last updated: 2026-09-19. Scope: this repository only.

## Durable context

- Owner: Slava. Personal tool and learning project; enjoys C#.
- Buxfer-inspired home: net worth, calendar-month income/expenses, largest expense
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
- Earlier inspection found 28 two-row transfer pairs sharing Buxfer IDs and three
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
  Actual Buxfer CSV remains outside the app.
- M2 committed and pushed as `576db1f` on main. M3 was committed and pushed as
  `ea77985` on 2026-09-19: schema v2 provenance plus pre-migration backup, Buxfer preview/apply,
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

## Resume here

See docs/PLAN.md for the M4 checkpoint and README.md for verified commands. Next
slice is M5 recurring templates/reminders. The synthetic test app is closed.

## Context maintenance

Product truth belongs in docs/SPECIFICATION.md; technical reasoning belongs in
docs/ARCHITECTURE.md; progress belongs in docs/PLAN.md. Keep this handoff compact.
Label assumptions, timestamp evidence, and replace stale facts rather than append
contradictory entries. Do not copy the user's transaction details into this file.
