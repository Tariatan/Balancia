# Balancia specification

Version 0.1 — 2026-09-17. Requirements are derived from the project conversation.
“Confirmed” means agreed with the user. “Default” means a proposed, reversible
implementation choice; it must not be reported as explicitly user-confirmed.

## 1. Purpose and scope — confirmed

A single-user personal finance ledger that replaces the owner's CSV workflow.
Windows provides full management. Android provides read-only balances and history
search. Both operate on local data; synchronization may be delayed and manually
refreshed. No online service is required for ordinary use.

Initial accounts: UBS, Cash, Revolut. All money is CHF. Include dated opening
balances, manual transactions, CSV migration, categories, and recurring reminders.

Out of scope: bank integration, multi-user access, currency conversion, credit
cards/debts, transaction splits, budget limits/rollovers, savings goals, forecasts,
external notifications, hosted backend, and subscription billing.

## 2. Home and balances — confirmed

The Windows overview uses a top navigation bar and shows account rows plus a
total in the net-worth card. Income and expenses sit beside that card. Below,
transaction history occupies the wide left column; largest expense categories
and upcoming/overdue recurring payments stack on the right. Amounts omit the
CHF label in the interface because the ledger has only one currency.

The overview offers All, This Week, This Month, This Year, and Custom From/To
date filters. This Month is the initial selection. Week means Monday through
Sunday; custom endpoints are inclusive. The selected period changes income,
expenses, largest expense categories, and visible transaction history. Net worth
and its account balances remain current across selections; recurring reminders
continue to show the next unresolved occurrences.
The overview history is paged in groups of 100 transactions matching its selected
period, with Previous page and Next page controls. Double-clicking
an overview row opens the transaction edit form for that record. The history
card header provides add and delete actions for the selected row.
The overview history filter panel provides search, advanced filters, and paging
alongside the same visual table used by the history card.
History amounts are bold; income is green, expense is red, and transfer is dark
blue. The overview history follows the bottom edge as the window changes height.
Overview history fills its available height without an outer page scrollbar.
Selected list rows use a light background so their text and colored values remain
readable. The Windows window minimum size is 1280 × 1280.
The Windows desktop stores its last usable width, height, and screen position in
`window.json` beside the selected local data folder and restores them on the next
start. An unavailable or malformed settings file does not prevent startup.

The Overview period controls are All, This Week, This Month, This Year, and
Filter. Filter opens the history search and filter fields in the overview.
The top navigation order is Overview, Settings. Account
management is available from the Accounts card on Overview. The
Settings header contains Import CSV, Export CSV, Export snapshot, and Restore
snapshot actions. Accounts can be added, edited, and
deleted from the Accounts card; accounts with transaction history are archived
instead of deleted.
Recurring template Add/Edit actions live in the Upcoming payments card on
Overview; there is no separate recurring payments tab. Settings places Add and
Edit above a list that fills the remaining page height. Category rows open the
edit form on double-click; the header provides add and archive actions.
Upcoming payment rows use the compact date/description and right-aligned amount
layout. The card header provides + and delete actions; delete asks for
confirmation, and double-clicking a row opens its edit form. The recurring edit
form does not expose archive state.
The Custom period Apply button aligns with its date inputs. Net-worth account and
total amounts are green when positive and red otherwise; overview income is green
and expenses are red. Transaction history gives Category more width than before
while sizing the Account column to the representative "Revolut → Revolut" text.

Account balance = opening balance + all signed movements after the opening point.
Net worth = sum of account balances. Opening balances are counted exactly once;
transfers cancel across accounts and are excluded from income/expense totals.
History filters do not change overview account balances or period totals.

Every successful add/edit/delete refreshes affected balances, category totals,
reminders, and visible history without restarting or manually reloading the app.
Month changes refresh the dashboard without requiring a transaction edit.

Defaults: show the five largest parent expense categories for the selected period,
with their subcategory expenses included. Use the device's local calendar date;
transaction dates have no time zone. Future expectations belong in reminders;
future-dated posted transactions are excluded from the initial form. Thus net
worth is all posted history, not a forecast. Historical pre-opening transactions
require revising the opening date/amount rather than silently double counting.

## 3. Transactions and accounts

Confirmed fields: Type (Expense / Income / Transfer), Description, Amount, Date,
Account, Category and optional Subcategory. Transfers use source and destination
accounts instead of one account. Each ordinary transaction has at most one
category/subcategory path. Description may be blank for ordinary transactions;
recurring templates require a nonempty description.
The transaction editor uses the same date picker as the overview filters. A new
transaction starts on Today; editing retains its stored date. The amount field
accepts `+`, `-`, `*`, and `/` expressions, evaluates them when focus leaves the
field and before saving, and rounds the result to CHF centimes.

Defaults: enter a positive amount and let the type determine the sign. Reject
zero amounts, fractional centimes, overflow, and same-account transfers. Accept
optional categories for existing uncategorized rows. Preserve imported Memo in
an optional notes field. Editing descriptions must not silently normalize existing
text; recurrence comparison remains exact, including case and whitespace.

Support adding, editing, removing, and searching transactions. A transfer is one
logical operation: editing/removing it always affects both accounts atomically.
Show one transfer in combined history; account-specific history shows its signed
effect on that account. Default removal UX: offer Undo; if not implemented in the
first slice, show a concise confirmation before deleting the whole transaction.

Provide account and category management (default). Referenced accounts/categories
can be archived, but cannot be deleted leaving orphaned transactions. Renaming
preserves identity and historical assignments. Opening balances remain editable;
an edit must immediately recalculate account and net-worth totals.

## 4. History and entry UX

Confirmed: fast add/change/remove/search; convenient form; responsive browsing of
thousands of entries; filtering by date or date span, category, and amount.

Defaults:
- Newest date first with stable ID as a tie-breaker; virtualized, incrementally
  loaded rows preserve scroll position where possible after edits.
- Description search is case-insensitive substring search. This is deliberately
  different from exact recurring-payment matching.
- Account, type, category/subcategory, inclusive date range, and inclusive absolute
  amount range filters combine with AND. Parent category includes its children.
- Clear filters in one action; show empty states and matching result count.
- Keyboard-first form: sensible focus/tab order, Enter to save, Escape to cancel,
  inline validation, searchable category selection, and retained last-used account.
- Editing a row opens its existing values. Failed saves retain entered values and
  show the error without committing partial data.

Proposed performance targets: on the development Windows machine, a synthetic
50,000-transaction dataset should give warm filtered/search pages and committed
dashboard refresh within 200 ms at p95, excluding human interaction and sync.
Measure in a release build; report hardware, query mix, and results. These are
targets, not measured guarantees. Keep database work off the UI thread.

## 5. Recurring reminders — confirmed

A template has a description, expected date (day/month/year), indicative CHF
amount, and a positive repeat interval of N months. The user manually maintains
templates and may change the expected date even when an occurrence is overdue.

An occurrence is satisfied when a posted transaction has exactly the template's
description and falls within that occurrence's calendar month and year. Day and
amount do not participate. Account/category are not additional matching keys.
Earlier-month payments do not count. On satisfaction, the next occurrence is N
months later: 1 monthly, 3 quarterly, 12 yearly. Never create ledger transactions
automatically. Unmatched past occurrences remain visible as overdue until paid or
manually rescheduled; do not silently skip them when the calendar advances.

Defaults for deterministic edge cases:
- Compute satisfaction from current transactions and a persisted schedule anchor;
  editing/deleting a matching payment can reopen an occurrence. Do not advance an
  irreversible cursor which loses the evidence needed to undo an edit.
- Repeated matches in one month satisfy that occurrence only once. Continue across
  subsequent scheduled months only when each has its own matching transaction.
- A manual reschedule moves the outstanding occurrence and establishes the new
  future cadence. Earlier historical occurrences are not regenerated. Store the
  reschedule boundary separately from payment-derived satisfaction.
- Preserve the desired day across months; clamp to month end when absent. January
  31 -> February 28/29 -> March 31, rather than drifting permanently to the 28th.
- Duplicate active template descriptions prompt the user to distinguish them;
  otherwise one payment would satisfy both. No fuzzy matching or amount heuristic.
- Show each active template's next unresolved occurrence, ordered by date, with
  overdue items first. A newly created template starts at its entered date, not at
  the beginning of imported history.

## 6. CSV migration

Confirmed import source is the local `transactions.csv` file. Columns observed:
ID, Date, Description, Currency, Amount, Type, Tags, Account, Status, Memo, IOU.

Import contract:
- Parse actual CSV quoting, empty values, UTF-8 BOM, and line endings. Dates use
  DD-MM-YY in this export; preview the resolved four-digit dates. Default century
  rule is 2000–2099 for two-digit years; other centuries require an explicit mapping.
- Parse decimal amounts invariantly and convert exactly to centimes. Current
  Expense values are negative, Income positive. Validate rather than silently
  invert inconsistent signs. Currency must be CHF for version 1.
- Map Tags of the form Parent / Child to the two-level hierarchy. Preserve blank
  category and standalone categories. Unknown multi-tag/deeper paths need a mapping.
- Group Transfer rows by CSV ID: require exactly two different accounts, same
  currency/date, opposite equal amounts. Do not pair by amount/date alone.
- Recognize the observed singleton Transfer rows described as Opening balance as
  account opening entries. Preview this classification; reject ambiguous openings.
- Preserve CSV IDs and source provenance. The repeated ID of a transfer is not
  a duplicate transaction. Reimporting identical data adds nothing. Changed data
  under an existing ID is a conflict to review, never a silent overwrite.
- Show preview counts, mappings, errors, and calculated account totals before
  applying. Commit the approved batch atomically. Invalid rows block the batch
  until corrected or explicitly excluded in the preview; never silently drop rows.
- Preserve Memo. Current rows are Cleared and IOU empty; unsupported status/IOU
  values must be surfaced rather than erased. Refund is not a supported product
  type; the owner corrected the source entry to Income.

Re-read and fingerprint the file for each import. A preview must not apply a
different file version. Reconcile signed source sums against imported account
balances, including openings once and both sides of transfers.

Export CSV writes all account opening balances and posted transactions from one
consistent ledger read. It uses one row per logical transaction (including a
transfer), an ISO date, signed expense amounts, and standard CSV quoting. The
columns are ID, Date, Type, Description, Amount, Account, DestinationAccount,
Category, and Memo. The export is independent of the 11-column import format.

## 7. Android, snapshots, and recovery

Confirmed: Android shows balances, transaction history/search, and a visible
snapshot update time. It never modifies financial data. Old snapshots and manual
refresh are acceptable. Google Drive is transport, not the active database.

Defaults: Windows explicitly exports a versioned snapshot to a selected local
sync folder. Android initially uses the system document picker to import a
downloaded/Drive-provided file; no assumption that Drive exposes a normal local
folder. Validate the actual provider/device behavior during the Android milestone.

Keep the working database in application-local storage outside synchronized
folders. Export a transactionally consistent snapshot to a temporary artifact,
then publish a complete immutable file. Include schema version, dataset identity,
revision, UTC creation time, and integrity metadata. Reject corrupt, incomplete,
wrong-dataset, or unsupported snapshots without losing the last usable copy.
Reject older revisions by default; deliberate restore is separate from refresh.
Transport failure never prevents Windows use or damages the active database.

Keep dated backups separate from the latest snapshot. Before schema migration,
take a recoverable backup. Test restoration before depending on migration.
Backup retention and snapshot encryption are open implementation choices; do not
claim encryption is supplied merely by using a sync folder. Private files never
belong in source control or routine diagnostic logs.
