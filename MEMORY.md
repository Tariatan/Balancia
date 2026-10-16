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

## Code that looks removable but is not

- 2026-10: `RefreshCalendarCulture` in [LocalizedCalendarDatePicker.cs](src/Balancia.Desktop/LocalizedCalendarDatePicker.cs) sets `FirstDayOfWeek` to a different day and back when the calendar already holds the target value. Assigning the same value does not redraw the week header. [stable]
- 2026-10: The `if (choice is null) return null;` guard in item templates (`InitializeAccountPanel` in [Accounts.cs](src/Balancia.Desktop/Accounts.cs), `InitializeCategoryPanel` in [CategoriesPanel.cs](src/Balancia.Desktop/CategoriesPanel.cs), also `RemindersPanel.cs` and `SettingsWindow.axaml.cs`) must stay although the item type is non-nullable: virtualized presenters can request a template with no item while scrolling. [stable]
- 2026-10: `FlowAnalytics.Current` ([FlowAnalytics.cs](src/Balancia.Core/FlowAnalytics.cs)) must be sorted by date ascending, because `BuildTrend` fills buckets in one forward pass and silently misassigns days otherwise. `ReadFlowAnalytics` in [FlowQueries.cs](src/Balancia.Storage/FlowQueries.cs) guarantees it with `ORDER BY l.date`; keep that clause and sort any other producer. [stable]

## UI tests

- 2026-10: Avalonia.Headless.XUnit in `tests/Balancia.Desktop.Tests` requires xunit v3, so all three test projects use xunit.v3. The DPAPI key store is Windows-only; tests inject the key through `UseSnapshotKey`. [stable]
- 2026-10: Opening a window with a backup or snapshot folder and no key shows the passphrase dialog, which blocks a headless test. Close such a window without showing it. [stable]

## Open work

- 2026-10: Android 0.3.1 APK (application version 5, target API 36) was published and hash-checked on 2026-10-03; that established packaging only. Next: verify Windows passphrase Save, encrypted export and restore via the picker, then run [Android device acceptance](docs/ANDROID.md) for initial pairing, same-key reimport, restart, changed phrase, wrong phrase, Cancel, damaged archives, runtime logs and Keystore outcomes.
