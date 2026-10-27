# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Versioning rule:

- Always bump Balancia.Desktop's application version whenever any project changes. Other projects are bumped only when their own implementation changes.
- **Major** - behavior visible to other system components changes (breaking)
- **Minor** - new backward-compatible feature
- **Patch** - backward-compatible bug fix only

## Balancia.Desktop

### [3.4.5] - 2026-10-26

#### Changed

- Windows restore applies the snapshot acceptance rule through the shared check in Storage; the rejection messages are unchanged. Bump Storage 7.3.0 and Android 0.5.2.

### [3.4.4] - 2026-10-26

#### Changed

- The window keeps one `CloseTimeExporter` per open ledger (replaced when the database location changes) instead of owning `ShutdownExporter`, `BackupRetention` and the backup baseline. Closing writes the same backup and snapshot as before. Bump Storage 7.2.0.
- Close-time log entries now carry the `Balancia.Storage.CloseTimeExporter` source context.

### [3.4.3] - 2026-10-26

#### Changed

- Error messages and the best-effort shutdown writes recognise database failures through `LedgerFailures` instead of SQLite exception types and error codes. No visible behavior change. Bump Storage 7.1.0.

#### Added

- Tests that pin the friendly error text for constraint violations, other database failures, argument errors, I/O failures and unknown failures.

### [3.4.2] - 2026-10-26

#### Changed

- The reminder list tints rows from two months out through `ReminderPresentation.IsBeyondNextMonth`, and the row colors count months with `ReminderSchedule.MonthsUntil`, instead of an inline rule in the panel. No visible behavior change. Bump Core 3.2.0 and Storage 7.0.1.

### [3.4.1] - 2026-10-26

#### Changed

- Resolve the overview period's dates, averaging unit and averaging start through `OverviewPeriods` in Core instead of private window methods. No visible behavior change. Bump Core 3.1.0 and Storage 7.0.0.

### [3.4.0] - 2026-10-18

#### Added

- Show/Hide toggle in snapshot passphrase dialogs to reveal or mask both passphrase fields together. Fields remain masked when the dialog opens.

### [3.3.0] - 2026-10-18

#### Changed

- Increase daily log retention from 10 to 180 files.
- Retain rolling backups for 180 local calendar days (today and the previous 179 days), keeping the newest three per day and at most 540 overall. Prune only after a successful backup export.

### [3.2.1] - 2026-10-18

#### Fixed

- Top expenditures shows selected subcategories separately, regardless of whether their parent categories are selected. Bump Storage to 6.0.1.

### [3.2.0] - 2026-10-16

#### Added

- Localize the CSV import preview summary in German, Russian and Ukrainian.
- Replaceable file and folder pickers on `MainWindow`, so import, export, restore and Settings flows run in headless tests.

#### Changed

- Move rules that need no controls out of `MainWindow`: `SettingsStore` (settings records and atomic file replacement), `SnapshotKeyService` with `ISnapshotKeyStore` (key lifecycle; Windows DPAPI behind the interface), `ShutdownExporter` and `BackupRetention` (close-time writes and the three-per-day, 21-overall policy).
- Settings changes build a new immutable `ApplicationSettings` record instead of passing positional optional arguments. Settings and the DPAPI key file are written through the shared atomic writer, so the settings file is now flushed to disk before it replaces the old one.
- Reminder presentation no longer has a "satisfied" state, following Core 3.0.0.
- Remove the unused "Choose a local CSV file." text; a picker that returns no local path counts as canceled.
- Tests: add suites for settings, key service, backup retention, shutdown exports, DPAPI store, logging setup, localization key parity, encryption flows, data transfer and the Settings window; the performance test now covers reminders, flow analytics and averages and fails when a read exceeds a 200 ms p95 budget.
- Bump Core 3.0.0 and Storage 6.0.0.

#### Fixed

- The close-time snapshot no longer replaces the last usable `Snapshot.balancia` when the ledger never loaded (for example a database from an older version).
- A corrupt or foreign DPAPI key file now offers passphrase setup again at startup, as intended, instead of a generic error dialog (`CryptographicException` was not handled).
- Export, import and restore result messages stay visible instead of being replaced by "Saved locally".
- The Overview no longer slows down as years of monthly reminders accumulate (see Storage 6.0.0).

### [3.1.1] - 2026-10-16

#### Changed

- Harness/Documentation update.

### [3.1.0] - 2026-10-15

#### Added

- Group transaction history visually by day with alternating pale blue and ivory row backgrounds, preserving selection and hover highlights.
- Shade reminders through next month pale ivory and later reminders pale blue. Alternate ivory and blue category bands by parent, with children sharing their parent's background.

### [3.0.2] - 2026-10-15

#### Fixed

- Create close-time backups only when the active ledger changed during the session. Keep the newest three backups per local calendar day and 21 overall, pruning only after a successful encrypted export.
- Bump Storage to 5.0.1.

### [3.0.1] - 2026-10-15

#### Changed

- Tests only, no production change: Core, Storage and Desktop test projects use Arrange/Act/Assert with `Method_Precondition_Outcome` names, with repeated cases merged into theories.
- Desktop tests open windows through shared workspace helpers and close them on dispose, so a failing test cannot leave a window open.
- Storage tests share `SqliteScript` and `TestClock` helpers; the opt-in performance test skips instead of passing when `BALANCIA_PERF` is not set.

### [3.0.0] - 2026-10-15

#### Changed

- **Breaking:** snapshot restore always requires the matching passphrase; plaintext snapshots are rejected and the unencrypted-snapshot branch is removed.
- **Breaking:** databases at schema version 1 or 2 no longer open, because the migrations are removed.
- Bump Storage 5.0.0.

#### Fixed

- CSV import accepts only exact `Type` names; numeric text, comma lists, padded and differently cased values are rejected as an unsupported transaction type.

### [2.14.2] - 2026-10-14

#### Fixed

- Fine-tune chart tooltip popups.

#### Changed

- Scroll the transactions list to the top when moving to the next or previous page.

### [2.14.1] - 2026-10-14

#### Changed

- Bump Core 2.3.1 and Storage 4.2.1.
- Log flow-aggregation overflow in the trend chart instead of in Core.

### [2.14.0] - 2026-10-14

#### Changed

- Switch to Cryptography.ProtectedData.

### [2.13.7] - 2026-10-14

#### Changed

- Optimize TransactionsPanel, UiHelpers, WindowSettings, WindowsSnapshotKeyStore classes.

### [2.13.6] - 2026-10-14

#### Changed

- Optimize Settings, SettingsWindow, SnapshotPassphraseWindow, StatCard classes.

### [2.13.5] - 2026-10-14

#### Changed

- Optimize Overview, ReminderPresentation, RemindersPanel classes.

### [2.13.4] - 2026-10-14

#### Changed

- Optimize FlowChart, Language, LocalizedCalendarDatePicker classes.

#### Removed

- Logging helper.

### [2.13.3] - 2026-10-14

#### Changed

- Optimize DataTransfer, DescriptionSuggestions, Dialogs, Encryption, FiltersPanel, FilterState classes.

### [2.13.2] - 2026-10-14

#### Changed

- Optimize CsvImportPreviewWindow: wire button handlers in XAML, build the preview message from named parts, and disable Cancel while the import runs.
- Optimize Backup and Categories classes.

### [2.13.1] - 2026-10-14

#### Changed

- Rename classes.
- Optimize Analytics class: split chart updates into focused methods and resolve the income/expense focus in a single pass.

### [2.13.0] - 2026-10-13

#### Added

- Display the running application version at the right of the top bar, using the same font size and color as the Settings version footer.

### [2.12.3] - 2026-10-13

#### Changed

- Optimize Accounts class.

### [2.12.2] - 2026-10-13

#### Added

- Introduce headless UI test project `Balancia.Desktop.Tests`.

#### Changed

- MainWindow can be created with an explicit data folder and settings file.
- The clock behind "today" is injectable, and the timer's day-change check is a named method.

### [2.12.1] - 2026-10-13

#### Changed

- MainWindow XAML uses shared styles for cards, icon buttons and text roles, and drops unused navigation styles and wrapper panels.
- MainWindow startup, lifecycle and shortcut handling split out of the constructor; overview data reads run concurrently; unused status field removed.
- Income, expense and average cards are a reusable StatCard control; reminder row colors and footer totals moved into ReminderPresentation.

### [2.12.0] - 2026-10-12

#### Changed

- Replace Recent category suggestions with Regular: show up to two matching category paths most used in saved transactions dated today or the previous 34 days. Regular appears first, matches parent/subcategory text, and refreshes after Add another transaction.
- Accepting a suggestion no longer persists selection history; canceled selections do not affect ranking. Localize Regular in all four desktop languages.

### [2.11.4] - 2026-10-12

#### Changed

- LocalizedCalendarDatePicker class optimized.

### [2.11.3] - 2026-10-12

#### Changed

- CsvImportPreviewWindow class optimized.

### [2.11.2] - 2026-10-10

#### Fixed

- Keep parent category checkboxes checked only when all their subcategories are checked; unchecking a subcategory clears its parent without changing sibling selections.

### [2.11.1] - 2026-10-10

#### Fixed

- Scroll the selected category suggestion into view when navigating the list with the Up and Down keys.

### [2.11.0] - 2026-10-10

#### Added

- Remember all accepted category suggestions in the active ledger across restarts, including selections made before canceling the dialog.

#### Fixed

- Update Recent immediately in Add transaction, including Add another transaction; match remembered paths by parent or subcategory text without a five-category limit.

### [2.10.0] - 2026-10-04

#### Added

- Display the Desktop application version at the bottom right of Settings.
- Categories panel `#` button beside `+` selects all categories or clears selection when all are checked, with a localized label in English, German, Russian, and Ukrainian.

#### Changed

- Toggling a parent category checkbox also selects or clears all its subcategories.

### [2.9.2] - 2026-10-03

#### Fixed

- Clear Category and its suggestion state after a successful Add another transaction save, then focus the empty field for the next entry.

### [2.9.1] - 2026-10-03

#### Fixed

- Fill missing diagnostics for encryption setup/change/cancel, remembered-key loading, manual restore unlock, and skipped close-time exports. Shutdown logs include duration and backup retention counts; nested operations carry correlation IDs.
- Keep snapshot failure diagnostics free of parser/provider messages that may contain private input. Expected snapshot rejections remain Information-level events.

### [2.9.0] - 2026-10-03

#### Added

- Snapshot encryption setup and passphrase change in Settings, with confirmation and a 12-character minimum. Windows remembers only the derived key, protected by current-user DPAPI; passphrases and keys are excluded from settings JSON and logs.
- Manual snapshots, ten rolling close-time backups, and the fixed `Snapshot.balancia` close-time export are encrypted. Existing configured destinations prompt for setup at startup; canceling pauses exports without replacing existing files.
- Encrypted restore prompts when needed and retains support for older plaintext snapshots. Local SQLite databases, local recovery copies, and CSV exports remain plaintext.

### [2.8.0] - 2026-10-03

#### Added

- CSV import preview now shows a separate reminder count. Reminder validation and conflict messages are translated into English, German, Russian, and Ukrainian.

### [2.7.0] - 2026-10-02

#### Changed

- Trend and Timeline tooltips now use bold, colored table rows with right-aligned amounts. Trend shows its date range and green Income, red Expense, and blue Savings rows; Timeline shows the metric and current/previous comparison dates in the corresponding series colors.
- Reuse tooltip content while hovering within the same chart point to avoid unnecessary redraws.

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

### [3.2.0] - 2026-10-26

#### Added

- `ReminderSchedule.Resolve`: the first occurrence of a reminder whose calendar month has no payment, with its overdue flag. It throws `ArgumentOutOfRangeException` for a repeat interval of zero or less, which `SaveReminder` and CSV import already reject.
- `ReminderSchedule.MonthsUntil`: whole calendar months between two dates.

### [3.1.0] - 2026-10-26

#### Added

- `OverviewPeriod`, `DateRange` and `OverviewPeriods`: one place that resolves a period preset into its date range, averaging unit (`IntervalFor`) and averaging start (`AverageFrom`; only a custom range keeps its own start).
- `AverageInterval` and `AverageIntervals.CountPeriods`, moved from Storage; the period-count rules (calendar days, Monday-start weeks, months, years) are unchanged.

### [3.0.0] - 2026-10-16

#### Changed

- **Breaking:** remove `Reminder.Satisfied`; no reader ever produced `true`, because satisfied occurrences advance to the next one.
- Document that `LedgerSnapshot.Entries` is filled only by the full read.

### [2.3.1] - 2026-10-14

#### Changed

- Optimize Core classes without changing results.
- Core no longer logs transaction-draft validation rejections and flow-aggregation overflow; Storage and Desktop log them instead.
- Reject expressions nested deeper than 64 parentheses and parse unary minus chains iteratively.
- `FlowAnalytics.Trend` throws `ArgumentOutOfRangeException` for an undefined `FlowInterval` instead of falling back to month buckets.

### [2.3.0] - 2026-10-12

#### Changed

- Filter ranked category usage candidates by typed text before choosing at most two Regular entries; retain other matching choices without duplicates.

### [2.2.0] - 2026-10-10

#### Changed

- Keep remembered matching categories in Recent even when they would otherwise rank as the top match.

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

### [7.3.0] - 2026-10-26

#### Added

- `ViewerSnapshotImport` with `IViewerSnapshotHost`, `PassphraseReason` and `ViewerImportOutcome`: the viewer's whole import sequence (remembered key, passphrase prompt, retry after a wrong passphrase, validate and swap, remember the key), moved out of the Android activity so it runs in unit tests. Log entries from this flow now read "Viewer ..." instead of "Android ...".

#### Changed

- Windows restore and the viewer import share one acceptance check for a snapshot of another dataset or an older revision; the messages are unchanged.

### [7.2.0] - 2026-10-26

#### Added

- `CloseTimeExporter`: the best-effort close-time encrypted backup (only when the ledger changed since `MarkLoaded`) and fixed snapshot (only for a ledger that loaded), moved from Desktop with their rules unchanged. Failures are logged and never thrown.
- `BackupRetention` (internal), moved from Desktop: newest three backups per local day for 180 days, 540 overall.

### [7.1.0] - 2026-10-26

#### Added

- `LedgerFailures` (`IsDatabaseFailure`, `IsConstraintViolation`, `IsUnreadableDatabase`): classifies database failures so Desktop and Android no longer match SQLite exception types or error codes. Storage still throws the same exceptions.

#### Changed

- Snapshot rejection logging uses `LedgerFailures.IsUnreadableDatabase` instead of its own copy of the error codes.

### [7.0.1] - 2026-10-26

#### Changed

- `ReadReminders` finds each next occurrence through `ReminderSchedule.Resolve` in Core. Occurrences, overdue flags and exact-description payment matching are unchanged.

### [7.0.0] - 2026-10-26

#### Changed

- **Breaking:** `AverageInterval` moved from `Balancia.Storage` to `Balancia.Core`; `ReadFlowAverages` takes the Core type and counts periods through `AverageIntervals.CountPeriods`. Averages are unchanged.

### [6.0.1] - 2026-10-18

#### Fixed

- Keep explicitly selected subcategories separate in filtered top-expenditure totals, including selections across multiple parents and selections containing both parents and children.

### [6.0.0] - 2026-10-16

#### Added

- `LedgerReader` and `LedgerSummary`: a read-only way to open a ledger (SQLite read-only mode) with summary, search and identity reads, for viewers.
- `SnapshotAuthenticationException` for a wrong passphrase or damaged file, and `CsvImportConflictException` for CSV import conflicts.

#### Changed

- **Breaking:** `SnapshotImporter.Import` returns a `LedgerReader` instead of a `LedgerStore`, and, like a Windows restore, refuses a snapshot of another dataset or an older revision than the copy already at the target path (an unreadable copy is replaced).
- **Breaking:** wrong-passphrase authentication throws `SnapshotAuthenticationException` and CSV import conflicts throw `CsvImportConflictException`; both derive from `InvalidOperationException`, where the first previously was `InvalidDataException`.
- **Breaking:** an active reminder's duplicate check, CSV import and payment matching all compare the exact description. Descriptions that differ only in letter case are no longer duplicates.
- Transaction descriptions are trimmed when saved and when imported from CSV.
- `AtomicFileWriter` is public so Desktop and Android share one atomic write.
- `ReadSnapshot` reuses the Desktop overview read instead of a second copy of the top-category rule; CSV preview reads only the ledger revision.
- Log classification no longer depends on operation names.
- Restore keeps the newest three plaintext recovery copies.
- Bump Core 3.0.0.

#### Fixed

- Snapshot export validates the staged database against its manifest, so a database this build cannot open no longer replaces the last usable snapshot.
- Reading reminders takes one pass over the ledger instead of one scan per month per reminder: 5.3 to 6.0 s before and 8.3 ms p95 after, for 20 monthly reminders since 2020 over 50,000 synthetic transactions.
- Deleting an account marks its imported opening balance as locally modified, so a later import reports a conflict instead of silently skipping it.

### [5.0.1] - 2026-10-15

#### Added

- Expose the ledger dataset ID and revision for backup change detection.

### [5.0.0] - 2026-10-15

#### Changed

- **Breaking:** plaintext ZIP snapshots are no longer importable and are rejected as an unsupported format. `SnapshotEncryption.ReadInfo` returns a non-null `SnapshotEncryptionInfo`, and `RestoreSnapshot`, `ValidateSnapshot` and `SnapshotImporter.Import` require a `SnapshotKey`.
- **Breaking:** schema migrations and the `.pre-v2-*.bak` backup are removed. `LedgerStore.Initialize` creates the full schema in an empty file and rejects any `user_version` other than `SchemaVersion` (3), so a database still at version 1 or 2 no longer opens.
- Optimize `SnapshotKey`: named layout constants, a single salt decoder, and malformed salts reported as `InvalidDataException`.
- Split `LedgerStore` partials by feature (`Accounts`, `Categories`, `Descriptions`, `FullSnapshot`, `Reminders`, `Schema`, `Transactions`, `Logging`) and move the shared SQL helpers into `LedgerSql`.
- Ledger total validation sums per account and per month inside SQLite instead of reading every movement, with the same 64-bit overflow rejection.
- Flow analytics read the current and previous period with one query; the CSV import is split into focused helpers, with a typed `CsvRowType` replacing type strings.
- A viewer import no longer fails when its staging file cannot be deleted; the cleanup failure is logged.

#### Fixed

- CSV import matches the `Type` column by exact member name; `Enum.TryParse` had begun accepting numeric text, comma lists and padded names.

### [4.2.1] - 2026-10-14

#### Changed

- Log rejected transaction drafts at Information when saving (moved from Core).
- Document the date-ascending order guarantee of `ReadFlowAnalytics`.

### [4.2.0] - 2026-10-12

#### Changed

- Derive Regular category candidates from transaction counts within an inclusive 35-day window, with latest transaction date and alphabetical path breaking ties. Archived categories, transfers, opening balances, and future dates are excluded.
- Stop creating or writing category selection history. Existing optional history tables are left unused; no ledger schema or snapshot format change is required.

### [4.1.0] - 2026-10-10

#### Added

- Persist unlimited category selection history by category ID and selection order in an optional schema-3 table. Renames retain history; archived categories are hidden and deleted categories are removed. Existing ledgers start with empty selection history.

### [4.0.1] - 2026-10-03

#### Fixed

- Add correlated viewer import start/validation/replacement/failure logs and authentication/export/restore milestones. Wrong/missing keys and malformed archives are expected rejections; unexpected file failures are errors.
- Exclude raw snapshot parser/provider exception messages; record failure type and HResult. Add tests for correlation, rejection severity, previous-copy preservation, and absence of secrets/financial content in captured logs.

### [4.0.0] - 2026-10-03

#### Changed

- **Breaking:** snapshot export requires a key and writes a versioned AES-256-GCM envelope over the entire ZIP, including its manifest. Older viewers must be updated to read new exports; legacy plaintext snapshots remain importable.
- Derive portable keys with PBKDF2-HMAC-SHA256 (600,000 iterations, random 32-byte salt); authenticate header metadata and use a fresh 12-byte nonce per export. Verify authentication before parsing/extracting the archive.
- Keep plaintext export staging in the local temporary directory, write only ciphertext beside the destination, and atomically replace the prior export after success. Read manifest metadata from the same consistent SQLite backup as the embedded database.
- Import validates and extracts the same archive once, cleans up failed staging, and preserves the last working database on wrong-passphrase, tampering, or validation failure.

### [3.3.0] - 2026-10-03

#### Added

- CSV export/import now includes reminder templates, preserving expected-date anchors, exact descriptions, indicative amounts, repeat intervals, and archived state. Reminder rows use the existing nine-column header, with schedule metadata in Memo; export reads reminders and ledger data from one consistent SQLite transaction.
- Reminder imports commit atomically with transactions, exclude reminder amounts from account totals, detect duplicate active descriptions and conflicting schedules, and track unchanged reimports. Local edits or deletion of an imported reminder prevent a later import from silently replacing that change.

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

### [0.5.2] - 2026-10-26

#### Changed

- The snapshot import sequence runs in Storage's `ViewerSnapshotImport`; the activity supplies the Keystore key cache, the passphrase dialog and the status texts. Screens and messages are unchanged. Application version is now 9. Bump Storage 7.3.0.

### [0.5.1] - 2026-10-26

#### Changed

- Classify a damaged or non-SQLite snapshot as a rejection through `LedgerFailures` instead of matching SQLite error codes. Application version is now 8. Bump Storage 7.1.0.

### [0.5.0] - 2026-10-18

#### Changed

- Increase daily log retention from 10 to 180 files.

### [0.4.0] - 2026-10-16

#### Changed

- **Breaking:** the application id is `com.triatan.balancia.android` (was `com.companyname.Balancia.Android`); it installs as a new app. Application version is now 6.
- Opt out of OS backup (`allowBackup="false"`) so the plaintext viewer database is not copied to Google backup, and remove the unused `INTERNET` permission.
- Open the viewer database through the read-only `LedgerReader`; the status line no longer loads the whole ledger.
- Reject a snapshot of another dataset or an older revision than the copy on the device, as a Windows restore does; match the wrong-passphrase case by exception type instead of message text.
- Bump Storage 6.0.0.

#### Added

- Per-account balances and the import time on the status screen.

#### Fixed

- Search results are scrollable, and a note shows when only the newest 50 of more matches are listed.

### [0.3.2] - 2026-10-15

#### Changed

- Snapshot import always unlocks with a key; the unencrypted-snapshot path is removed. Bump Storage 5.0.0.

### [0.3.1] - 2026-10-03

#### Fixed

- Activate app-private daily file logging before viewer startup, retaining ten daily files. Previously shared-library and cleanup logs had no Android sink.
- Record saved-copy startup, picker/copy/import, unlock/cancel/retry, Keystore cache outcomes, and transaction search results/failures without logging secrets, financial contents, document URIs, or search text. Application version is now 5; API 36 is unchanged.

### [0.3.0] - 2026-10-03

#### Added

- Encrypted snapshot import with a masked passphrase prompt, retry/cancel behavior, and automatic unlock on later imports. Android Keystore wraps the remembered derived key in app-private, non-backed-up storage.
- Reopen the previous local viewer copy on startup. Rejected imports display an error and keep the previous copy; incoming archives are removed after import.

### [0.2.0] - 2026-09-30

#### Changed

- Updated to Balancia.Storage's renamed `TransactionsFilter`/`ReadTransactions` API.

### [0.1.0] - 2026-09-28

#### Added

- Changelog introduced, tracking the current released version.
