# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Versioning rule:

- **Major** - behavior visible to other system components changes (breaking)
- **Minor** - new backward-compatible feature
- **Patch** - backward-compatible bug fix only

## Balancia.Desktop

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

### [2.0.0] - 2026-09-30

#### Changed

- **Breaking:** renamed `RecurringTemplate`, `RecurringReminder`, and `RecurringSchedule` to `ReminderTemplate`, `Reminder`, and `ReminderSchedule`.

### [1.2.0] - 2026-09-28

#### Added

- Changelog introduced, tracking the current released version.

## Balancia.Storage

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
