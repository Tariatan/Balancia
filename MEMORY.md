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

- `EditDialog` (MainWindow.Dialogs.cs) supports an optional `validate: Func<string?>?`
  callback, checked before `prepareSave`/the store call; a returned message is shown
  inline instead of letting the store's `ArgumentException` throw. Applied to every
  Add/Edit dialog that has free-text input and a client-derivable store check: Recurring
  template (description/amount/interval), Add/Edit transaction (category path format +
  existence + archived, account opening date), Add/Edit category (name format, archived
  parent, subcategories-must-stay-top-level), Add/Edit account (name, opening date not
  in the future). Left as store-only (no `validate`) where the check genuinely needs a
  DB round trip not covered by `snapshot` in memory — recurring-template description
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

