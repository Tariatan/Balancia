# Balancia project memory

Scope: this repository only. Entries are `- YYYY-MM: fact and why` with the annotations defined by the edit-memory-file skill. Verification notes record evidence from the date shown, not rerun checks.

## Read first

- [Technical Design Description](docs/Technical_Design_Description.md) owns product behavior and design decisions.
- [AGENTS.md](AGENTS.md) owns operational rules, including versioning and documentation ownership; [README.md](README.md) owns build/run commands.
- [CHANGELOG.md](CHANGELOG.md) records releases and implemented changes.
- [PLAN.md](docs/PLAN.md) is maintained exclusively by the user.

## Decisions

- 2026-10: `CsvImportRowValidator.ParseType` matches `CsvRowType` member names exactly. `Enum.TryParse` is unsafe here because it accepts numeric text, comma lists and padded names. Export writes the same enum names, so renaming a member changes the file format. [stable]
- 2026-10: Regular suggestions use two matching category paths ranked by saved transaction frequency over 35 days (transaction dates; 35 rather than 30 gives monthly and slightly delayed payments a chance). The owner rejected unlimited persisted Recent because it would gradually include nearly every category, and wants to try Regular in daily use. Success: Regular surfaces the expected monthly and ordinary choices and two entries suffice; otherwise revise the window or ranking. [TRIAL: 2026-10 - 2026-12]
- 2026-10: Windows is the only writer; Android is a read-only balance/history viewer. Stale snapshots and manual refresh are acceptable, and Google Drive is user-managed snapshot transport. Android transport/device acceptance remains open (see [Android Context](docs/Technical_Design_Description.md)). [stable]
- 2026-10: The owner chose to leave local databases, recovery copies and CSV exports plaintext. Transported snapshots and rolling backups are encrypted; canceling key setup pauses exports with no plaintext fallback. Changing the passphrase affects future exports only, older files keep their old phrase. See the TDD's snapshot lifecycle and the Desktop 2.9.0 / Storage 4.0.0 changelog entries. [stable]

- 2026-10: Reminder matching and the duplicate-description check both use the exact description (case-sensitive, trimmed); transaction descriptions are trimmed on save so they can match. This follows the "exact description" invariant in [AGENTS.md](AGENTS.md). [stable]
- 2026-10: Android import follows the Windows restore rules: it rejects a snapshot of another dataset and an older revision of the same one, and replaces only an unreadable copy. Starting over with a different ledger means clearing the app's storage. [stable]

- 2026-10: Localization keys are the English text (`Get("English text")`), kept deliberately for readable call sites and an English fallback. Rewording a string means renaming its key in all four resx files; `LocalizationTests` fails on a missing or extra key, an empty value, or a changed placeholder. [stable]

## Code that looks removable but is not

- 2026-10: `RefreshCalendarCulture` in [LocalizedCalendarDatePicker.cs](src/Balancia.Desktop/LocalizedCalendarDatePicker.cs) sets `FirstDayOfWeek` to a different day and back when the calendar already holds the target value. Assigning the same value does not redraw the week header. [stable]
- 2026-10: The `if (choice is null) return null;` guard in item templates (the account, category, reminder and transactions cards: [AccountsCard.axaml.cs](src/Balancia.Desktop/AccountsCard.axaml.cs), [CategoriesCard.axaml.cs](src/Balancia.Desktop/CategoriesCard.axaml.cs) and [RemindersCard.axaml.cs](src/Balancia.Desktop/RemindersCard.axaml.cs), [TransactionsCard.axaml.cs](src/Balancia.Desktop/TransactionsCard.axaml.cs) and `SettingsWindow.axaml.cs`) must stay although the item type is non-nullable: virtualized presenters can request a template with no item while scrolling. [stable]
- 2026-10: List rows use [Choice<T>](src/Balancia.Desktop/Choice.cs) items. A `ListBox` row's accessibility name is the item's `ToString()`, so a raw `Account` or `Reminder` record as the item makes a screen reader announce `Account { Id = ..., Name = ... }`. The card tests assert the row names, so keep the `Choice<T>` wrapper when changing a list. [stable]
- 2026-10: `FlowAnalytics.Current` ([FlowAnalytics.cs](src/Balancia.Core/FlowAnalytics.cs)) must be sorted by date ascending, because `BuildTrend` fills buckets in one forward pass and silently misassigns days otherwise. `ReadFlowAnalytics` in [FlowQueries.cs](src/Balancia.Storage/FlowQueries.cs) guarantees it with `ORDER BY l.date`; keep that clause and sort any other producer. [stable]

- 2026-10: `UiText.Resources` caches its resource sets for the whole process. A test that disposes one (for example with `using var`) breaks every later localization lookup and makes unrelated tests fail. [stable]

## UI tests

- 2026-10: Avalonia.Headless.XUnit in `tests/Balancia.Desktop.Tests` requires xunit v3, so all three test projects use xunit.v3. The DPAPI key store is Windows-only; tests inject the key through `UseSnapshotKey`. [stable]
- 2026-10: Opening a window with a backup or snapshot folder and no key shows the passphrase dialog, which blocks `OpenAsync`. Show the window and answer the dialog with `WaitForDialogAsync<T>`, or close it without showing it. [stable]
- 2026-10: Window tests answer file and folder pickers through the `OpenFilePicker`, `SaveFilePicker` and `FolderPicker` properties and replace DPAPI with `FakeKeyStore` (`CreateWindowWithKeyStore`). An operation that is still open, such as the CSV preview, completes only after its dialog closes, so start it without awaiting, drive the dialog, then await it. [stable]
- 2026-10: `TextBox.TextChanged` and `CalendarDatePicker.SelectedDateChanged` are dispatcher-driven: they run after the setter returns, so a test that checks their effect (the filter row tone colors) must call `Dispatcher.UIThread.RunJobs()` first. To hook a change synchronously, for example to simulate a focus loss while the row is showing values, subscribe to `PropertyChanged` instead; a test that hooks the dispatched event passes vacuously. [stable]
- 2026-10: Test code that raises focus events needs `FocusChangedEventArgs(InputElement.LostFocusEvent)`; a plain `RoutedEventArgs` throws `InvalidCastException`. A public xunit theory cannot take an internal type such as `DialogSize` or `FlowFocus` (CS0051), so pass the enum name as a string and parse it in the test. [stable]

## Open work

- 2026-10: Android 0.4.0 (application id `com.triatan.balancia.android`, application version 6, no backup, no permissions, read-only `LedgerReader`) builds in Release but is not published or tested on a device. The last published APK is 0.3.1 (2026-10-03, hash-checked). Next: verify Windows passphrase Save, encrypted export and restore via the picker, then run [Android device acceptance](docs/ANDROID.md) for initial pairing, same-key reimport, restart, changed phrase, wrong phrase, Cancel, damaged archives, an older snapshot, long search results, runtime logs and Keystore outcomes. The new id installs as a separate app, so the first import asks for the passphrase again.
