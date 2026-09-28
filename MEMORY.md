# Balancia project memory

Last updated: 2026-09-28. Scope: this repository only.

## Durable context

- Windows performs all writes. Android checks balances and searches history;
  stale snapshots and manual refresh are acceptable. Offline local operation.
- Recurrence: exact description within scheduled month/year, ignoring amount/day;
  every N months; editable expected date; unpaid occurrences remain overdue.
- Agreed stack: C#/.NET, Avalonia, SQLite; one-way Google Drive snapshot transport.
  Windows dependencies are pinned and tested; Android transport remains unverified.

## Current evidence

- Git initialized on main on 2026-09-17. Private GitHub repository created at
  https://github.com/Tariatan/Balancia.

## Resume here

## Context maintenance

Product truth and technical reasoning belong in
docs/Technical_Design_Description.md; remaining/upcoming tasks belong in
docs/PLAN.md. Keep this handoff compact. C# formatting preferences from
CategoriesPanel and RemindersPanel are recorded in docs/Coding Guidelines.md.

