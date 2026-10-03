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

- 2026-10-03: Reminder CSV release metadata: Desktop 2.8.0 and Storage 3.3.0,
  with matching Added changelog entries. Only these implementations changed;
  Core and Android versions remain unchanged.

- 2026-10-03: CSV now includes Reminder rows alongside openings/transactions.
  The nine-column header is unchanged: reminder:<id>, expected-date anchor,
  exact description, indicative amount, empty account/destination/category,
  and Memo JSON with IntervalMonths/Archived. Export reads templates and ledger
  in one SQLite read transaction. Imports exclude reminders from balances,
  track fingerprints atomically, reject conflicts, and mark locally edited/
  deleted imported reminders as modified. Preview count and validation errors
  translated in all four desktop languages. Locked restore and Release build
  passed; runtime CSV reminder roundtrip has not been exercised.

- 2026-10-02: Chart tooltip visuals moved to a shared compiled data template in
  App.axaml, bound to ChartTooltipData/ChartTooltipRow. FlowChart now supplies
  only formatted row data; AXAML owns spacing, bold fonts, and amount alignment.
  Desktop Release build and diff check passed; native hover review is pending.
- 2026-10-02: Trend tooltips now show a date heading and bold colored two-column
  rows with right-aligned amounts, using the displayed series and existing signs.
  Timeline uses the same table with a metric heading and per-row current/comparison
  dates. Desktop Release build and diff check passed;
  native hover/visual review remains pending.
- 2026-10-02: Last 30 days preset follows Year in all four languages; it spans
  today and the previous 29 days and defaults Trend to Week. Startup selects it
  only when the unfiltered current calendar month has no transactions; otherwise
  Month remains the default. Desktop Release build passed; native startup and
  filter interaction review remains pending.
- 2026-10-02: Logging release versions: Desktop 2.5.0, Core 2.1.0, Storage
  3.2.0, each with a dated Added changelog entry. Android implementation and
  version remain unchanged; dependency locks reflect the shared Serilog packages.

- 2026-10-02: Logging expanded under check-logging skill across Core/Storage/
  Desktop. Core validates/rejects at Information, parser diagnostics at Debug,
  aggregation overflow at Error. Storage logs operation IDs, start/completion/
  failure and elapsed time around writes, initialization, CSV, and snapshots.
  Desktop logs location/language changes and classifies expected validation
  rejections as Information. Fatal remains at application boundaries. No sinks
  in libraries; shared Serilog dependency added and dependent locks refreshed.
  Locked Desktop restore, Release build (zero warnings/errors), and diff check
  passed. Synthetic startup log showed matching initialization operation IDs,
  schema-ready milestone, duration, and shutdown; failure paths not executed.

- 2026-10-02: Desktop logging uses Serilog 4.3.1 / File sink 7.0.0 (Automaton
  versions), configured before UI startup. %LOCALAPPDATA%/Balancia/log contains
  balancia-yyyyMMdd.log; daily rolling, shared append, retention 10, no size roll.
  Startup/shutdown, saves/imports, settings failures, backup/snapshot failures,
  and unhandled exceptions are logged. Locked restore, Release build (zero
  warnings/errors), and diff check passed. Two synthetic native launches grew
  the same daily file from 498 to 997 bytes with both sessions intact. Retention
  and midnight rolling are configured but not time-shift verified.

- 2026-10-02: Description autocomplete release metadata: Desktop 2.4.0 and
  Storage 3.1.0, with matching Added changelog entries. Both implementations
  changed; Core and Android versions remain unchanged. Diff check passed.

- 2026-10-02: Add/Edit transaction Description now has case-insensitive prefix
  suggestions from nonempty saved transaction descriptions, recent dates first.
  Up/Down selects, Tab accepts and advances focus, click accepts, Escape closes.
  New descriptions remain allowed; save-and-continue refreshes local history.
  Locked restore, Release build (zero warnings/errors), and diff check passed.
  Synthetic dialog opened, but native Description/Tab interaction remains
  unverified because the UI tool could not reliably target the owned dialog.

- 2026-10-02: Pending reminders beyond the five-day urgency window are blue in
  the next calendar month and green in later months; red overdue and bold yellow
  imminent styling take precedence. Month offsets include year boundaries.
  Desktop Release build and diff check passed; native visual review is pending.
- 2026-10-02: Pending reminder dates, descriptions, and amounts are red when
  overdue and golden yellow for today through today + 5 days inclusive, with bold
  text in all three columns for these groups. Satisfied
  and later reminders retain normal text. Desktop Release build and diff check
  passed; native visual review remains pending.
- 2026-10-02: Reminders totals use TOTAL and uppercase UPCOMING THIS/NEXT MONTH
  labels in all four desktop languages. Desktop Release build and resource/diff
  checks passed; native visual review remains pending.
- 2026-10-02: Desktop reminder subtotal now selects Upcoming this month when
  unsatisfied current-month occurrences exist; otherwise Upcoming next month.
  It sums only unsatisfied occurrences in the selected calendar month. Both
  labels are localized in English/German/Russian/Ukrainian. Locked restore,
  Release build (zero warnings/errors), and diff check passed; native branch
  switching has not been manually verified.

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
- Release metadata (2026-10-02): Desktop version is 2.7.0 for chart tooltip
  tables; 2.6.0 added the Last 30 days
  filter and startup fallback; 2.3.0 added reminder due-date
  colors and bold urgency styling; 2.2.1 updated the Reminders labels
  update; 2.2.0 added the default account
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

