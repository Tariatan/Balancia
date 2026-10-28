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

### [3.11.0] - 2026-10-28

#### Added

- Transaction type dropdown between the end date and description filters, with localized All, Expenses, Income, and Transfer choices. Selection refreshes the shared overview filter and Clear filters resets it to All.

### [3.10.3] - 2026-10-28

#### Fixed

- Add/Edit requires an existing subcategory for income and expenses; transfers remain category-free. Category suggestions show only matching Category / Subcategory paths, including Regular suggestions.

### [3.10.2] - 2026-10-28

#### Fixed

- Reminder duplicate validation ignores letter case in the dialog, ledger saves, and CSV imports (Core 3.6.1).

### [3.10.1] - 2026-10-28

#### Changed

- No change of its own; the version follows Storage 8.1.1.

### [3.10.0] - 2026-10-28

#### Added

- Color themes. Settings has a **Theme** picker next to the language picker. It lists the built-in `Balancia` theme and the `*.json` files in a `themes` folder beside `settings.json`; choosing one changes the colors at once and is remembered (`Theme` in `settings.json`, absent for the built-in theme).
- A theme file is `{ "name": "...", "colors": { "<role>": "#RRGGBB" } }`. The name is optional (the file name is used), role names are the `Palette` roles and ignore letter case, and `#AARRGGBB` is accepted. A role the file leaves out keeps its built-in color. An unknown role or an unreadable color is ignored with a log warning; a file that is not a JSON object with a `colors` object, a duplicate name and the name `Balancia` are skipped. A saved theme whose file is gone starts as the built-in theme. A separate data folder offers only the built-in theme.
- `Palette.Apply` shows a set of colors on the shared brushes, and every open window is redrawn after a change so the hand-drawn charts follow too.
- `ColorThemeFile` and `ColorThemeCatalog`, with 27 tests of reading, the catalog and its rules; palette tests of `Apply`, and window tests of starting with a saved theme, switching from Settings, the missing-file fallback, the separate data folder and the translated label.

#### Changed

- A new window always starts from the colors of its saved theme, so a theme chosen earlier in the same process does not leak into it.

### [3.9.2] - 2026-10-28

#### Changed

- The XAML takes its colors from `Palette`: the application registers every role as the resource `Palette.<Role>` and the XAML files use `{DynamicResource Palette.<Role>}` instead of hex values. New roles: `WindowBackground`, `HeaderBar`, `OnHeaderBar`, `Border`, `AccentSurfaceHover`, `AccentSurfacePressed`, `AccentTintLight`, `AccentTintMedium` and `Error` (dark red). Every value is the one it had, so there is no visible change; `White` and `DarkRed` are now `Surface`, `OnAccent`, `OnHeaderBar` and `Error`.
- Error text in the code and in the dialogs shares one `Palette.Error` brush.

#### Added

- `PaletteTests` (6 tests): every role is an application resource holding the shared brush, the roles are named after their fields, every `Palette.` key in the XAML is a role, no color literal, `Brush.Parse`, `Color.Parse` or named color remains outside `Palette.cs` and `Language.cs`, and controls declared in XAML (window background, error text, overview cards) hold the palette brushes.
- The tests that use `Palette` run on Avalonia's UI thread, because a brush belongs to the thread that creates it.

### [3.9.1] - 2026-10-28

#### Changed

- `Palette` holds the colors the C# code uses, by role, as shared brushes: text, accent, positive and negative amounts, the warning and transfer colors, row tints, selection, popups and the chart colors. The transaction, reminder, account, category, filter and suggestion code, `FormControls` and the charts take their colors from it. No visible change: every value is the one it had.
- `ReminderPresentation.ColorFor` is `BrushFor` and returns the palette brush. `FlowChartSeries` carries a brush instead of a color string.

#### Removed

- `RowBrushes`, `BalanceBrushes` and the brush fields of `TransactionRows`, now in `Palette`.

### [3.9.0] - 2026-10-28

#### Changed

- Restoring a snapshot asks for the passphrase again after a wrong one, inside the same restore, and shows "The snapshot passphrase is incorrect or the file is damaged." in the passphrase dialog. Cancel ends the restore and leaves the ledger untouched. Before, a wrong passphrase ended the restore with an error dialog and the snapshot had to be picked again.
- The restore uses `SnapshotUnlock`, the unlock sequence the Android import also uses. A passphrase typed during a restore is still not remembered, and the remembered key is still the one that encrypts exports and backups.

### [3.8.0] - 2026-10-28

#### Changed

- Importing a CSV file replaces the whole ledger instead of appending to it. When the ledger holds data, the preview says "This import replaces the current ledger: {0} accounts, {1} transactions and {2} reminders will be deleted. A recovery copy is saved first.", the button reads **Replace data**, and Cancel is the default button and has the focus. An empty ledger keeps **Apply import** and no warning.
- After an import the status line reads "Replaced the ledger with {0} imported entries." (it used to count unchanged entries), and a default account that is not in the imported file is cleared in the settings.
- A file with no rows shows "The file has no rows to import." and cannot be applied.
- The new texts are in all four languages.

### [3.7.4] - 2026-10-28

#### Changed

- The CSV import preview lists each category once, in the spelling the ledger uses (`Food / Lunch`), however the file spells its path. Before, `Food/Lunch` and `Food / Lunch` showed as two categories.

### [3.7.3] - 2026-10-28

#### Changed

- `OverviewView.StartAsync` picks the period the Overview opens on (this month, or the last 30 days when the current month has no transactions); the window no longer reads the month's transactions for that. `DisplayDate` can only be set by the view. No visible change.

#### Added

- `OverviewViewTests`: 13 tests of the view on its own, without a window: the opening period, the totals and scope label, `ShowChange` reaching the period buttons and the filter inputs, and the period, account and page choices changing the state and raising `ReloadRequested`.

### [3.7.2] - 2026-10-28

#### Changed

- `OverviewView` applies the period, filter, clear-all, account and category filter and page choices itself, through `OverviewState`, and raises `ReloadRequested` when the new choice needs other data; the window answers it with a refresh. A page change still scrolls the transactions to the top once the new page is shown. No visible change.

#### Removed

- The `FiltersPanel.cs` part of `MainWindow` and the filter-toggle and paging methods of the account, category and transaction panels, now inside `OverviewView`.

### [3.7.1] - 2026-10-28

#### Changed

- The Overview is an `OverviewView` user control that owns its cards, the `OverviewState` and the loaded `OverviewData`; `MainWindow.axaml` hosts it. The window loads through `LoadAsync`, draws with `Render` and `RenderInPlace`, reads `Data` and `DisplayDate` for its dialogs, and shows status text through `ShowStatus`. No visible change.

#### Removed

- The `Overview.cs`, `Analytics.cs` and `FilterState.cs` parts of `MainWindow`, now inside `OverviewView`.

### [3.7.0] - 2026-10-28

#### Changed

- The transaction form checks its accounts, the date and the typed category path with `TransactionRules` instead of its own copy, so it applies the same rules as the ledger store.
- A category path that names no category is refused with "Choose an existing category path. No category "Food / Dinner"." (the path in the ledger spelling); before, with the first sentence only.
- The form accepts a category typed with any letter case and any spacing around the separator (`food/lunch`, `Food /Lunch`), as the store always did; it used to ask for the stored spelling.
- An archived account that the edited entry already uses is accepted, and a new entry on an archived account is refused with "Choose an active account for new movements.".

#### Added

- Leaving the category field rewrites what was typed in the one spelling the ledger shows (`food/lunch` becomes `Food / Lunch`; a path that names no category is only tidied). Suggestions find a path typed with another spacing around the separator.

#### Removed

- `TransactionFormRules.CategoryPathError`, replaced by `TransactionRules.CheckCategoryPath`.

### [3.6.0] - 2026-10-28

#### Added

- The Add and Edit account dialog refuses a name another account already has (letter case ignored) and an opening date later than the account's first transaction, with the rule's message. Before, both showed only after saving, the name as "This change conflicts with existing data".
- The Add and Edit reminder dialog refuses a description another active reminder already has (exact match, as reminders are matched).
- The account Delete confirmation shows "An account with transactions cannot be deleted. Archive it instead." in the dialog instead of after you confirm.
- Dialog tests for the duplicate account name and the duplicate reminder description, and that a description differing only in letter case is accepted.

#### Changed

- The account and reminder dialogs and the transaction form's opening date check use `AccountRules` and `ReminderRules` instead of their own copies. The existing messages are unchanged.

### [3.5.2] - 2026-10-27

#### Fixed

- Chart model tests explicitly select English so date-label assertions do not depend on the language left by another test.
- Overview filter synchronization tests wait for the database refresh after the controls update, preventing temporary database cleanup failures and overlapping filter actions.

### [3.5.1] - 2026-10-27

#### Fixed

- Clearing the filters from a custom date range returned to this month but kept the trend zoom of the custom range, so the trend chart and its interval label showed Day or Week buckets of the old range. The zoom now returns to this month's, as selecting This month does. A preset period keeps the zoom you chose.

#### Changed

- `OverviewState` tells the window what a change leaves to do (`OverviewChange`: reload the data, show the period buttons, trend zoom, dates, inputs or the account and category checkboxes again). The window applies that in one place instead of each filter action calling its own subset. Applying typed values no longer re-syncs the account and category checkboxes, which it never changed.

#### Added

- Tests of the flags each state change returns, and of the Clear fix. Four window tests drive the real controls: clear from a custom range, select a period, apply typed dates, and check a parent category.

### [3.5.0] - 2026-10-27

#### Added

- Adding or renaming a category to a name that another category under the same parent already has (letter case ignored) is refused in the dialog with its own message. Before, the save failed with a general "conflicts with existing data" message.
- A dialog test for the duplicate name message.

#### Changed

- The Add and Edit category dialogs, the delete confirmation and the transaction form's category path check use `CategoryRules` instead of their own copies of the rules. The delete confirmation no longer asks the store two separate questions. The messages are unchanged.

### [3.4.21] - 2026-10-27

#### Changed

- The filter row is the `FilterRow` user control (its buttons, date and amount inputs, tone colors and period highlight were part of the window). The window now only applies what the row reports to the overview state. Filtering, the period highlight, the search shortcuts and the localization behave as before.
- Removed two pieces of state the window no longer needs: a "filters visible" flag that was always true, and the `updatingFilterControls` flag, whose checks in the account and category toggles could never fire.

#### Added

- 31 tests of the filter row, including that every period button and input raises the right event, that Enter, leaving an input, clearing the search and a closing calendar commit, that showing values never commits, which period is highlighted, the dark and muted input colors, and that an amount that is not a number fails to read.

### [3.4.20] - 2026-10-27

#### Changed

- The trend and timeline charts are the `TrendCard` and `TimelineCard` user controls, and the numbers behind them are built by `FlowChartModel` (it was part of the window). The charts, their tooltips and the wheel zoom behave as before.

#### Added

- 44 tests of the chart model and cards, including which flow the filter narrows the charts to, the empty messages, the bucket labels and the one-year label boundary, the cumulative timeline against the previous period (also for very long ranges), the wheel direction, that a zoom redraws the chart and stops at the coarsest and finest interval, and that both charts get their data in the window.

### [3.4.19] - 2026-10-27

#### Changed

- The edit dialog is the `EditDialog` class (it was a method of the window, in `Dialogs.cs`) and the friendly error text is `FriendlyErrors`. The dialog's behavior is unchanged.

#### Added

- 19 tests of `EditDialog`, including that a failed save keeps the dialog open with the entered values, that closing and Escape are blocked while saving, that Add another refreshes while the dialog stays open and returns the focus, that the overview is refreshed once after a save, and which button is the default.

### [3.4.18] - 2026-10-27

#### Changed

- The rules that keep the window's operations from overlapping are `OperationGate` (one operation at a time, a call that arrives while another runs is dropped, refreshes requested meanwhile collapse into one) and `OperationRunner` (runs an operation, tells its host when work starts, succeeds, fails and ends, logs a failure instead of letting it escape, and afterwards runs the pending refresh). The window supplies the host (the controls, the status line and the error dialog), and the `busy` field, the pending-refresh flag and the `Run` method are gone. Closing the window, the keyboard shortcuts and the day-rollover refresh read the runner's busy state. No visible behavior change.

#### Added

- 20 tests of the gate and the runner, including that a call during an operation is dropped, that several refresh requests during an operation give one refresh afterwards (also after a failure), that the operation is no longer busy when the controls come back, and that a failure is reported to the right dialog without escaping.

### [3.4.17] - 2026-10-27

#### Changed

- The fields of the add and edit transaction dialog are built by `TransactionForm`, which is given what it needs (`TransactionFormInputs`: the transaction being edited, today, the accounts and categories, the default and last used account, the recent descriptions and the regular category paths, and a way to read the regular paths again) and holds no window, store or settings reference. It knows what the fields start with, when they are valid, what they save and how "Add another" resets them. The window only reads the data, opens the dialog and saves what the form returns. No visible behavior change.
- The shared dialog controls (`Field`, `Input`, `DateInput`, `Text`, `Row`, `ChoiceBox`, `SelectedValue`) moved from private members of the window to `FormControls`, and `UiHelpers.cs` is gone.

#### Added

- 28 tests that drive the form on its own: the field order and labels, the starting values for a new and an edited transaction, which accounts are offered and which one starts selected, showing the destination for a transfer and the category otherwise, reading the draft (an amount typed as an expression, the category path, the destination only for a transfer), unreadable amounts and dates, validation, the amount being evaluated when it loses focus, and what "Add another" resets and keeps.

### [3.4.16] - 2026-10-27

#### Fixed

- In the add transaction dialog, a description saved with "Add another transaction" is offered by the description suggestions from then on. The suggestions used to keep the list read when the dialog opened, so a description saved a moment ago was not offered until the dialog was opened again. `DescriptionSuggestBox.Remember` adds it, first among its matches.

#### Added

- Tests for it, including one that saves a transaction with "Add another transaction" and then types the start of its description in the real dialog.

### [3.4.15] - 2026-10-27

#### Changed

- The two suggestion popups of the add and edit transaction dialog are their own controls, `DescriptionSuggestBox` (earlier descriptions that start with what was typed) and `CategorySuggestBox` (category paths ranked into regular, top match and the rest), instead of static builders in the window. Behavior is unchanged.

#### Added

- 27 tests of the two controls: matching, the 20 match limit, opening only while focused, Up and Down with clamping, Tab (and Shift+Tab, which moves the focus on and closes the popup), Escape, clicking, losing focus, not reopening right after an accept, the section headers and highlighted row, and the category box's reset.

### [3.4.14] - 2026-10-27

#### Changed

- The rules of the add and edit transaction form that need no controls live in `TransactionFormRules`: which accounts and categories the form offers (archived ones only when the edited entry uses them), the account it starts on, how the typed category path is read and checked, the date check against the opening dates of the account and of a transfer's destination, and how the entered values become a draft (only a transfer keeps its destination). The dialog calls them and keeps only the controls. No visible behavior change.

#### Added

- 38 tests of those rules, including two behaviors of the existing form that they now pin: the starting account does not fall through to the last used one when the configured default is not offered, and a category typed as `Food/Lunch` is rejected because paths are stored as `Food / Lunch`.

### [3.4.13] - 2026-10-27

#### Changed

- The transactions card is its own user control, `TransactionsCard`: `Render(page, offset)` draws one page shaded by day with the range heading and the previous and next buttons, rebuilding the rows only when they changed and keeping the selected row when it is still on the page; add, remove, edit and page requests are raised as events. `MainWindow` keeps the page, the dialogs and the saving. `TransactionRows` builds the row that the list and the remove confirmation share. No visible behavior change.
- The transactions list template returns nothing for a missing item, as the other list templates do. This is a guard added during the move; no failure was observed without it.

#### Added

- 21 tests that drive `TransactionsCard` on its own: the range heading and both page button limits, the empty state, day shading, the sign, color and text of each kind of row (including the account effect), the accessibility name, keeping or rebuilding the rows and the selection, localizing, the four buttons, selection, a real double-click and scrolling to the top.

### [3.4.12] - 2026-10-27

#### Changed

- The top expenditures card is its own user control, `TopExpendituresCard`: `Render` draws the largest expense categories as bars scaled to the biggest one, and `RenderIfChanged` skips the redraw when the totals and heading are unchanged. The rule for which category the heading names (a single filtered category that has subcategories) is `OverviewState.FilteredParent`. A rename of that category now refreshes the heading even when the totals are unchanged. No other visible behavior change.
- Removed the window's `QuietText` and `MutedTextBrush`, which only this card used.

#### Added

- 11 tests that drive `TopExpendituresCard` on its own (rows and amounts, bar scaling, the plain and scoped heading, the empty message, forced and conditional redraws) and 4 tests of `OverviewState.FilteredParent`.

### [3.4.11] - 2026-10-27

#### Changed

- The categories card is its own user control, `CategoriesCard`: `Render` draws the category tree with group tints, indented subcategories, archived markers and a filter checkbox per category, and add, toggle-all, archive, remove, edit and filter-toggle requests are raised as events. `MainWindow` keeps the dialogs, saving, the filter and the top-expenditures card. No visible behavior change.
- `ArchivedLabel` holds the "(archived)" marker text that the window and the card share.

#### Added

- 12 tests that drive `CategoriesCard` on its own: rows, group tints, subcategory indent, the archived marker, the accessibility name, checkboxes following the filter, syncing without raising toggles, a user toggle selecting the row, the four buttons, selection and a real double-click.

### [3.4.10] - 2026-10-27

#### Fixed

- Rows in the Reminders and Accounts cards announce their description or account name to screen readers again. Since 3.4.6 (reminders) and 3.4.9 (accounts) a row was announced as the technical text of its record, for example `Account { Id = ..., Name = ... }`. List rows use the shared `Choice<T>` item again, as the panels did before they became cards.

#### Added

- Tests for the accessibility name of a reminder row and an account row.

### [3.4.9] - 2026-10-27

#### Changed

- The accounts card is its own user control, `AccountsCard`: `Render` draws the balances, the default-account highlight, the net worth and a filter checkbox per account, and add, archive, remove, edit and filter-toggle requests are raised as events. `MainWindow` keeps the dialogs, saving and the filter. No visible behavior change.
- `BalanceBrushes` and `GridLayout` hold the balance colors and the grid column helper that cards share, replacing private members of the window.

#### Added

- 13 tests that drive `AccountsCard` on its own: rows, empty state, net worth color (zero counts as not positive), the default-account highlight, checkboxes following the filter, syncing without raising toggles, a user toggle selecting the row, the three buttons, selection and a real double-click.

### [3.4.8] - 2026-10-27

#### Changed

- What the user chose on the Overview (period, filter, transactions page, trend zoom) lives in `OverviewState`, which holds no controls and reads no clock. The period buttons, filter row, account and category checkboxes, paging and trend zoom call it instead of changing window fields, and `RefreshCore` asks it for the query to load. No visible behavior change.

#### Added

- 39 tests of the state without a window: period selection, the custom range made by typed dates, Last 30 days keeping its dates open, clearing filters, account and category toggling with subcategory and parent rules, paging, the scope rule, the query and the trend zoom.

### [3.4.7] - 2026-10-27

#### Changed

- The overview reads everything it shows through `OverviewLoader` into one immutable `OverviewData`, replacing six separate window fields (snapshot, reminders, transactions page, analytics, averages and the applied filter). A refresh is one load and one assignment, and a failed read leaves the previous data in place as before. No visible behavior change.

#### Added

- Tests for the loader: the data matches the individual store reads, averages use the whole ledger for periods without their own window and the custom span for a custom range, the averaging unit follows the period, and the page offset is applied.

### [3.4.6] - 2026-10-27

#### Changed

- The reminders card is its own user control, `RemindersCard`, with `Render(reminders, today)` and add, remove and edit events; `MainWindow` keeps the dialogs and saving. No visible behavior change.
- The two alternating row tints are shared through `RowBrushes`.

#### Added

- Tests that drive `RemindersCard` on its own (rows, totals, empty state, the two-months-out tint, the three events including a real double-click). The test driver's `Find` also looks inside user controls, whose names live in their own name scopes.

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

### [3.6.1] - 2026-10-28

#### Fixed

- Reject active reminder descriptions that differ only in letter case, preserving trimming, self-edit and archived-template exceptions. Payment matching remains case-sensitive.

### [3.6.0] - 2026-10-28

#### Added

- `CategoryRules.FindChild`, the one definition of "the category with this name under this parent": letter case is ignored for every character. `TransactionRules.FindByPath` uses it.
- Tests of `FindChild`.

### [3.5.0] - 2026-10-28

#### Added

- `TransactionRules` (account, date and category path checks for saving a transaction over plain lists of accounts and categories, with `FindByPath` and the messages; a category path is found by its names, so letter case and the spacing around '/' do not matter) and `SavedTransaction`, the accounts and category an edited transaction already uses.
- `CategoryRules.Prettify`, which gives a typed category path the one spelling the ledger shows (`Food/Lunch` becomes `Food / Lunch`), and tests of the transaction rules and of `Prettify`.

#### Changed

- `CategorySuggestionRanking.Build` reads the query with `Prettify`, so `food/lunch` and `Food /Lunch` find `Food / Lunch`.

### [3.4.0] - 2026-10-27

#### Added

- `AccountRules` (save, delete and transaction date checks over a plain list of accounts) and `ReminderRules` (description, amount, interval and duplicate description checks), with their messages.
- `Account.FirstTransactionDate`: the date of the account's earliest entry other than its opening balance, or null. It defaults to null.
- 27 tests of the account and reminder rules.

### [3.3.0] - 2026-10-27

#### Added

- `CategoryRules`: the category path parser and the save and delete checks, over a plain list of categories, with their messages. This includes a check for a name already used under the same parent.
- `Category.HasTransactions`, true when a ledger entry uses the category. It defaults to false.
- 31 tests of the category rules.

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

### [8.1.2] - 2026-10-28

#### Fixed

- CSV preview rejects active reminder descriptions that differ only in letter case, consistent with save validation.

### [8.1.1] - 2026-10-28

#### Removed

- Everything that wrote the `import_sources` table, which nothing has read since an import started to replace the ledger: the per-entry insert at import (with the source fingerprint and the copy of the source rows), the five `locally_modified` updates in saving and deleting transactions, accounts and reminders, `CsvImportGroup.Fingerprint` and `CsvImportRow.Fields`. Saving and deleting touch one table less.
- The join of `VerifyImportedTotals` with that table: after the ledger is cleared, the sum of an account's movements must equal the file's total for it, which is the same check.

#### Changed

- The empty `import_sources` table stays in the schema (version 3), because dropping it needs a new schema version and a migration of databases, snapshots and backups; the next import deletes the rows an older version left in it. The design doc says so.
- The rollback tests inject their failure with a trigger on transfer entries instead of on `import_sources`.

#### Added

- Tests that the next import deletes the leftover rows and that an import and later edits write none.

### [8.1.0] - 2026-10-28

#### Added

- `SnapshotUnlock.RunAsync` and `ISnapshotUnlockHost`: the sequence that finds the key for a snapshot file. It uses the remembered key when it matches the file, otherwise asks for the passphrase, asks again when the operation reports a `SnapshotAuthenticationException`, and returns `UnlockResult.Canceled` when the user declines. It disposes every key it holds. `PassphraseReason` moved to this file.
- 11 tests of the unlock sequence on its own.

#### Changed

- `ViewerSnapshotImport` is built on `SnapshotUnlock`; `IViewerSnapshotHost` now extends `ISnapshotUnlockHost` and keeps its members, so the Android host needs no change. Behavior and the 13 `ViewerSnapshotImportTests` are unchanged.

### [8.0.0] - 2026-10-28

#### Changed

- Breaking: `ApplyCsvImport` replaces the ledger. In one transaction it deletes every account, category, transaction, reminder and import record (the dataset id stays and the revision advances) and imports the file, so a failure leaves the ledger as it was. A repeated import is no longer a no-op, and a changed row, a locally edited entry, an omitted source ID or a deleted account no longer produce a conflict; the file wins.
- Breaking: `ImportResult` is `(Imported, RecoveryCopyPath)` instead of `(Added, Unchanged)`.
- Before a ledger that holds data is replaced, a plaintext recovery copy is saved next to the database as `.pre-import-<time>-<id>.bak`; the three newest are kept, counted apart from the pre-restore copies. If the copy cannot be written nothing is deleted. A stale preview is refused before any copy is made.
- `CsvImportPreview.Existing` (`ExistingLedger`: accounts, transactions without opening balances, reminders) says what applying would delete; `IsEmpty` is true for a fresh ledger.
- A file with no importable rows is refused by the preview with "The file has no rows to import.", so a bad file cannot empty the ledger.

#### Removed

- The reconciliation code that replacing makes unreachable: the import source comparison and its conflicts, the unchanged count, the conflict with an existing reminder or opening balance, and the archived category conflict. `import_sources` is still written.

### [7.5.2] - 2026-10-28

#### Fixed

- CSV import matched category names with SQLite `COLLATE NOCASE`, which folds ASCII only, so a row with `épicerie` created a second category next to an existing `Épicerie` that `SaveCategory` would have refused. Import now reads the categories once and finds them with `CategoryRules.FindChild`, as saving a transaction does; the categories it creates are added to that list so later rows find them. An archived category still stops the import with the same message.
- The import summary (`ImportSummary.Categories`) lists a path once however its separator is spelled, in the form `Category / Subcategory`.

### [7.5.1] - 2026-10-28

#### Changed

- `SaveTransaction` and `SaveTransactionWithCategoryPath` run `TransactionRules` over the accounts and categories read in the same transaction, replacing the per-account and per-category SQL lookups. The messages and exception types are unchanged, except that a path that names no category gets "Choose an existing category path. No category "Food / Dinner"." (the path in the ledger spelling). `SaveTransactionWithCategoryPath` still resolves a path by its names, whatever the letter case and spacing. The account read leaves out balances, so a save does not scan the ledger.

### [7.5.0] - 2026-10-27

#### Changed

- `SaveAccount`, `DeleteAccount` and `SaveReminder` run `AccountRules` and `ReminderRules` over the accounts and active reminders read in the same transaction; a transaction's date check uses `AccountRules` too. The messages and exception types are unchanged, except that a name another account already has is refused with its own message instead of a database constraint error.
- The accounts read fills `Account.FirstTransactionDate` with one more grouped query. The 50,000-transaction latency test still passes.
- CSV preview reports a reminder row's blank description, non-positive amount and non-positive repeat interval with the rule's own message ("Enter a description.", "Enter amount greater than zero.", "Repeat interval must be positive.") instead of "Invalid reminder fields." or "Invalid reminder schedule.". A malformed reminder memo still reads "Invalid reminder schedule.". Applying an import checks a duplicate active reminder description with `ReminderRules`; the message is unchanged.

#### Added

- Tests of the duplicate account name, `FirstTransactionDate` and the rule message on a duplicate reminder.

### [7.4.0] - 2026-10-27

#### Changed

- `SaveCategory` and `DeleteCategory` run `CategoryRules` over the categories read in the same transaction. The messages and exception types are unchanged, except that a name already used under the same parent is now refused with its own message instead of a database constraint error.
- The category read fills `Category.HasTransactions`.

#### Removed

- `CategoryHasSubcategories` and `CategoryIsUsedByTransactions`: the delete confirmation took their answers, and `DeleteCategory` already enforced both rules.

#### Fixed

- CSV import compared reminder descriptions without regard to letter case, in the file and against existing reminders. They are now compared exactly, as `SaveReminder` and reminder matching do, so `Rent` and `rent` can both be imported.
- CSV preview now reports a row dated in the future, including an opening balance, as an error; before, it could be applied although a transaction or opening date in the future is refused everywhere else. Reminder rows keep their schedule date.
- CSV preview, `SaveTransaction` with a category path and the transaction form show one message for a category path with more than two levels or an empty side.

#### Added

- Tests of the duplicate name rule, the `HasTransactions` flag, future-dated CSV rows, invalid CSV category paths and case-differing CSV reminders.

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