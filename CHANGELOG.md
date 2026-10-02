# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Versioning rule:

- **Major** - behavior visible to other system components changes (breaking)
- **Minor** - new backward-compatible feature
- **Patch** - backward-compatible bug fix only

## Balancia.Desktop

### [2.6.0] - 2026-10-02

#### Added

- Added a Last 30 days filter after Year in all four desktop languages. The range includes today and the previous 29 days, with weekly Trend granularity by default.
- Startup selects Last 30 days when the current calendar month has no transactions; otherwise Month remains selected. Later filter changes do not trigger this fallback.

### [2.5.0] - 2026-10-02

#### Added

- Application logging with Serilog: daily append-only files in `%LOCALAPPDATA%\Balancia\log` beside `settings.json`, retaining the latest 10 files. Restarts share the day's file; no size-based splitting occurs.
- Logs for startup/shutdown, dialog saves, imports, location/language changes, settings failures, shutdown backups/snapshots, and unhandled application/UI/background-task exceptions. Expected validation rejections use Information; unexpected failures include exception details; unrecoverable application failures use Fatal.


### [2.4.0] - 2026-10-02

#### Added

- Description autocomplete in Add/Edit transaction suggests previously saved descriptions using case-insensitive prefix matching, with recently used descriptions first. Up/Down selects a suggestion, Tab accepts it and advances focus, mouse click accepts it, and Escape dismisses the list. New descriptions remain freely editable, and saving another transaction refreshes suggestions in the same dialog.

### [2.3.0] - 2026-10-02

#### Added

- Color pending reminder dates, descriptions, and amounts by due date: bold red for overdue, bold golden yellow for today through five days ahead, blue for next calendar month, and green for later months. Urgency colors take precedence over month colors, including across year boundaries.

### [2.2.1] - 2026-10-02

#### Changed

- Renamed Reminders' TOTAL NET label to TOTAL and capitalized UPCOMING THIS MONTH and UPCOMING NEXT MONTH in English, German, Russian, and Ukrainian.

### [2.2.0] - 2026-10-02

#### Added

- Highlight the default account row with a blue background slightly darker than normal selection, extending 2px on each side without adding text or shifting the row content.

### [2.1.0] - 2026-10-02

#### Added

- Focused date pickers now support keyboard Up/Down to adjust the selected date by one day.

#### Changed

- Keyboard Up and mouse wheel up increase the date; Down and wheel down decrease it. Empty dates, date limits, blackout dates, and open-calendar keyboard navigation are preserved.

### [2.0.1] - 2026-10-02

#### Changed

- Renamed the current-day filter from Day to Today in English, German, Russian, and Ukrainian, including startup and language changes.

### [2.0.0] - 2026-10-02

#### Changed

- **Breaking:** CSV import now accepts only Balancia's own nine-column export format, using ISO dates, explicit opening balances, and one row per transfer. Updated import preview wording in all four languages.
- **Breaking:** window geometry and application preferences now persist in one `%LOCALAPPDATA%\Balancia\settings.json`. The settings path stays fixed when the database location changes; legacy `window.json` and settings files beside custom databases are no longer loaded or migrated.
- Temporary `--data-dir` sessions no longer save application preferences or use configured backup and snapshot folders.

### [1.7.0] - 2026-09-30

#### Changed

- Renamed Overview UI elements, backing fields, and panel files (e.g. `MainWindow.OverviewHistory.cs` → `MainWindow.TransactionsPanel.cs`, `MainWindow.RecurringPayments.cs` → `MainWindow.RemindersPanel.cs`) to drop the redundant `Overview` prefix and align `recurring`/`history` naming with the `Reminder`/`Transactions` terminology used by Balancia.Core and Balancia.Storage.
- Updated reminder tooltips, dialog titles, and empty-state text (English, German, Russian, Ukrainian) to match the "Add/Delete selected/No … yet" wording already used by the Accounts, Categories, and Transactions panels.

### [1.6.0] - 2026-09-28

#### Changed

- Controls moved from code-built UI to XAML.

### [1.5.0] - 2026-09-28

#### Added

- Changelog introduced, tracking the current released version.

## Balancia.Core

### [2.1.0] - 2026-10-02

#### Added

- Structured logging for transaction-validation rejections, Debug diagnostics for rejected amount expressions, and Error details for monetary overflow during flow aggregation. Financial descriptions, notes, amounts, and expression input are not dumped; Core does not configure log files.


### [2.0.0] - 2026-09-30

#### Changed

- **Breaking:** renamed `RecurringTemplate`, `RecurringReminder`, and `RecurringSchedule` to `ReminderTemplate`, `Reminder`, and `ReminderSchedule`.

### [1.2.0] - 2026-09-28

#### Added

- Changelog introduced, tracking the current released version.

## Balancia.Storage

### [3.2.0] - 2026-10-02

#### Added

- Structured start/completion/failure logging with operation IDs and elapsed time for ledger writes, initialization, CSV preview/reconciliation, and snapshot export/validation/restore.
- Schema-ready, import result, and snapshot-restore milestones. Expected validation/import rejections use Information; unexpected failures include exceptions at Error. Storage uses the application logger without owning a file sink.


### [3.1.0] - 2026-10-02

#### Added

- Added `ReadRecentDescriptions` to retrieve distinct nonempty transaction descriptions for autocomplete, excluding opening balances and deduplicating case-insensitively. Results are ordered by latest transaction date, then ledger insertion order, using existing ledger records without a separate history table.


### [3.0.1] - 2026-10-02

#### Fixed

- Advance satisfied reminder occurrences regardless of whether their expected date has passed. An exact-description payment on October 2 now advances an October 10 monthly occurrence to November 10; matching still uses calendar month/year and ignores day and amount.


### [3.0.0] - 2026-10-02

#### Changed

- **Breaking:** replaced the eleven-column CSV import format with the exported `ID,Date,Type,Description,Amount,Account,DestinationAccount,Category,Memo` structure. The old header is rejected before writes.
- Import and export share the same column header. Import validates ISO dates, unique IDs, exact centime amounts, one explicit opening balance per account, and positive single-row transfers with distinct source and destination accounts.
- Transfers expand into two balanced movements within the existing atomic import transaction. Export/import roundtrip preserves account balances, transaction text, categories, and transfer direction; unchanged repeated imports remain a no-op.

### [2.0.0] - 2026-09-30

#### Changed

- **Breaking:** renamed `HistoryFilter`, `HistoryHit`, and `HistoryPage` to `TransactionsFilter`, `TransactionsHit`, and `TransactionsPage`; renamed `ReadHistory`/`ReadAllHistory`/`ReadHistoryAfter`/`FindHistoryOffset` to `ReadTransactions`/`ReadAllTransactions`/`ReadTransactionsAfter`/`FindTransactionOffset`; renamed `ReadRecurringReminders`/`SaveRecurringTemplate`/`DeleteRecurringTemplate` to `ReadReminders`/`SaveReminder`/`DeleteReminder`.
- **Breaking:** renamed the `recurring_templates` SQLite table to `reminder_templates` with no migration; existing databases must be reimported.

### [1.2.0] - 2026-09-28

#### Added

- Added category usage/subcategory lookup methods so the desktop UI can validate deletions before calling the store.

### [1.1.0] - 2026-09-28

#### Added

- Changelog introduced, tracking the current released version.

## Balancia.Android

### [0.2.0] - 2026-09-30

#### Changed

- Updated to Balancia.Storage's renamed `TransactionsFilter`/`ReadTransactions` API.

### [0.1.0] - 2026-09-28

#### Added

- Changelog introduced, tracking the current released version.
