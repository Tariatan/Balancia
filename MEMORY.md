# Balancia project memory

Last updated: 2026-09-17. Scope: this repository only.

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
  Specific packages and Android transport integration remain unverified.

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
- Installed SDKs observed: 8.0.302, 9.0.301, 10.0.201. No application scaffolding
  or package compatibility verification has been performed.

## Resume here

The specification and harness have been created. See docs/PLAN.md for the current
checkpoint. Both harness-check modes passed on 2026-09-17; CSV SHA-256 was unchanged.
These checks do not verify application behavior. Next implementation slice: validate/pin the SDK and Avalonia versions,
scaffold the Windows shell and domain tests, then implement accounts and atomic
ledger operations. Consult docs/ACCEPTANCE.md before adding financial behavior.

## Context maintenance

Product truth belongs in docs/SPECIFICATION.md; technical reasoning belongs in
docs/ARCHITECTURE.md; progress belongs in docs/PLAN.md. Keep this handoff compact.
Label assumptions, timestamp evidence, and replace stale facts rather than append
contradictory entries. Do not copy the user's transaction details into this file.
