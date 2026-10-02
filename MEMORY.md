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

- 2026-10-02: Reminder satisfaction now advances the occurrence immediately
  regardless of whether its expected date has passed. An exact-description
  payment on October 2 satisfies an October 10 occurrence and advances a
  monthly reminder to November 10. Month/year matching and schedule clamping
  remain unchanged. Locked restore, Release Desktop build (zero warnings/errors),
  and diff check passed; native reminder interaction has not been verified.

- 2026-10-02: Accounts highlights the default account row with a blue tint darker
  than normal selection, extending 2px on each side without shifting content or
  adding text. Desktop Release build and
  diff check passed; native visual review remains pending.
- 2026-10-02: Shared desktop date pickers support unmodified Up / wheel up (next day)
  and Down / wheel down (previous day) while the calendar is closed, preserving empty values,
  date limits, blackout dates, and open-calendar navigation. Locked restore,
  Release build (zero warnings/errors), and diff check passed. Native keyboard
  verification initially crossed October 1 / September 30 in both directions.
  Direction was then inverted at user request: native Up moved October 1 to 2,
  and wheel up moved October 2 to 3; the revised Release build passed.

- 2026-10-02: The Day period filter is now Today / Heute / Сегодня / Сьогодні,
  including initial XAML and language refresh. Chart Day granularity stays Day.
  Locked Desktop restore, Release build, and diff check passed; native visual
  review remains pending.
- Release metadata (2026-10-02): Desktop version is 2.2.0 for the default account
  highlight (2.1.0 added keyboard date adjustment; 2.0.1 added the Today filter)
  label correction; Storage remains 3.0.0 for the breaking settings/CSV changes.
  Matching dated changelog entries
  added. Core and Android versions remain unchanged. The owner confirmed the
  CSV export/import workflow successfully in Windows.
- CSV import (2026-10-02): only the nine-column Balancia export header is
  accepted, with ISO dates, explicit OpeningBalance rows, and one positive
  transfer row with source/destination accounts. The old eleven-column format
  is rejected before writes. Import/export share their header; transfer rows
  expand into balanced movements. Actual export-to-fresh-ledger roundtrip,
  unchanged reimport, invalid format/IDs/amounts, and atomic rollback are covered.
  Desktop Release build (zero warnings/errors), 82 storage tests, and diff
  checks passed. The CSV picker/preview was not manually exercised this turn.
- Settings consolidation (2026-10-02, revised): one settings.json stays in
  %LOCALAPPDATA%\\Balancia, storing preferences, the selected DB path, and
  window geometry. Settings retains database, backup, and snapshot folder selectors.
  Registry access and all legacy window/settings migration have been removed.
  --data-dir sessions do not write preferences or use configured backup/snapshot
  folders. Desktop Release build and diff checks passed; manual Settings
  interaction remains unchecked for this revision.
- `EditDialog` (MainWindow.Dialogs.cs) supports an optional `validate: Func<string?>?`
  callback, checked before `prepareSave`/the store call; a returned message is shown
  inline instead of letting the store's `ArgumentException` throw. Applied to every
  Add/Edit dialog that has free-text input and a client-derivable store check: Reminder
  template (description/amount/interval), Add/Edit transaction (category path format +
  existence + archived, account opening date), Add/Edit category (name format, archived
  parent, subcategories-must-stay-top-level), Add/Edit account (name, opening date not
  in the future). Left as store-only (no `validate`) where the check genuinely needs a
  DB round trip not covered by `snapshot` in memory — reminder-template description
  uniqueness, category/account "no longer exists" (concurrent-edit races), account
  opening date vs. first transaction date. Delete/Archive dialogs have no free-form
  input, so nothing to validate there.
- Settings moved out of `MainWindow.Settings.cs` into a standalone `SettingsWindow`
  (XAML + code-behind), matching the earlier Overview-into-XAML migration — done and
  manually verified 2026-09-28. `MainWindow.Settings.cs` now only opens it and holds
  the three folder-change actions `SettingsWindow` calls back into.
- Same pattern applied to the two other fixed-shape code-built dialogs: `ErrorDialog`
  (was `ShowErrorDialog`'s inline body) and `CsvImportPreviewWindow` (was the dialog
  built inline in `ImportCsv`) — both done 2026-09-28, build/tests verified, NOT yet
  manually clicked through (unlike Settings). `store` is now `internal` so
  `CsvImportPreviewWindow` can call `owner.store.ApplyCsvImport` directly.
  `EditDialog` (Add/Edit/Delete/Archive, all entities) is NOT a migration candidate —
  its field set varies per caller, no fixed layout to move to XAML.

## Context maintenance

Product truth and technical reasoning belong in
docs/Technical_Design_Description.md; remaining/upcoming tasks belong in
docs/PLAN.md. Keep this handoff compact. C# formatting preferences from
CategoriesPanel and RemindersPanel are recorded in docs/Coding Guidelines.md.

