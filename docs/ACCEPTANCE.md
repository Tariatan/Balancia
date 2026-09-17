# Acceptance scenarios

These are executable-test specifications and manual release checks, not test
results. Use synthetic examples; do not commit the owner's CSV as a fixture.
Scenario IDs connect implementation milestones to observable behavior.

| ID | Scenario | Expected outcome |
| --- | --- | --- |
| L01 | Open account at CHF 1,000; spend 20; receive 50 | Balance 1,030; income 50; expenses 20; opening excluded from flows |
| L02 | Transfer 100 from A to B | A -100, B +100, net worth unchanged, no income/expense impact |
| L03 | Edit transfer amount/accounts/date, then delete it | Both movements change together; deletion removes both; no orphaned movement |
| L04 | Simulate failure midway through a transfer | Whole operation rolls back, balances and revision unchanged |
| L05 | Add 0.10 and 0.20 income | Exactly CHF 0.30; reject excess precision/overflow/zero entry amounts |
| L06 | Edit opening amount/date | Balances update exactly once; monthly income remains unchanged; invalid overlapping history is surfaced |
| L07 | Restart app after committed changes | Same ledger and balances persist; failed writes never appear |
| H01 | Search mixed-case description and combine filters | Search is case-insensitive; date/amount endpoints inclusive; filters combine with AND |
| H02 | Filter parent category | Parent's own entries and child entries included, other categories excluded |
| H03 | Several transactions share a date across loaded pages | Stable order; no missing/duplicate rows in unchanged dataset |
| H04 | Save/edit/delete from history | Affected home totals and history refresh; failed save retains form values |
| H05 | Clock crosses month/year boundary | Current-month totals and reminder display refresh correctly |
| R01 | Rent expected Sep 25; exact-description payment Sep 1 for another amount | September satisfied; next date Oct 25 for interval 1 |
| R02 | September rent paid Aug 31, or Sep in another year | September occurrence remains unresolved |
| R03 | Case/whitespace differs from template description | No exact recurrence match; ordinary search may still find it |
| R04 | Two matching payments in September | Advance one occurrence only; do not also satisfy October |
| R05 | Quarterly due September; annual due September | On matching payment, next due December / September next year respectively |
| R06 | September unpaid when October starts | September remains overdue; no automatic payment or silent skip |
| R07 | Move overdue Sep 30 to Oct 1 manually | Outstanding occurrence moves; subsequent monthly cadence is Nov 1; old occurrence is not regenerated |
| R08 | Edit/delete the only matching payment | Recompute and reopen applicable occurrence; editing unrelated amount keeps it satisfied |
| R09 | Monthly anchor Jan 31 across leap/non-leap February | Clamp in February, restore March 31; annual Feb 29 clamps in non-leap years |
| R10 | Create two active templates with identical descriptions | Surface ambiguity; require distinct descriptions under default UX |
| I01 | CSV quoted comma/newline, BOM, empty description, notes | Correct records and preserved text, no naive comma splitting |
| I02 | Two transfer rows share ID, opposite amounts and distinct accounts | Exactly one logical transfer; opening singleton handled separately |
| I03 | Same ID reimported unchanged; then changed amount under same ID | Unchanged import is a no-op; changed data is a reviewable conflict |
| I04 | Missing transfer side, unknown type/currency, invalid precision/date | Preview explains error; no silent loss and no partial commit |
| I05 | Source changes after preview or import fails mid-batch | Abort/re-preview or roll back; ledger stays unchanged |
| I06 | Import current private export locally | Re-read hash; reconcile each account and total against source; do not commit private expectations |
| I07 | Opening balances plus ordinary transfers in one batch | Openings counted once; transfer net sum zero; neither inflates income |
| S01 | Export while transactions are being entered | Export represents one consistent revision, not mixed states |
| S02 | Import valid snapshot on Android | Balances/search reflect revision and visible export timestamp; no write UI |
| S03 | Truncated/corrupt/unsupported/wrong-dataset snapshot | Explain failure and retain previous usable copy |
| S04 | Older revision or interrupted refresh | No silent rollback or loss of old data |
| S05 | Offline Windows and Android startup | Both work locally; unavailable sync does not block the ledger |
| S06 | Backup, migrate, then restore in a test environment | Restored balances and records match backup; failed migration recoverable |
| P01 | Release build, 50,000 synthetic transactions | Record hardware and p95 query/refresh latency against proposed 200 ms target |

## Verification layers

- Core unit tests: exact money, transfer rules, recurring schedule and injected clock.
- SQLite integration tests: real temporary database, atomic failure paths, migrations,
  import idempotency, reconciliation, and snapshots. In-memory fakes are insufficient.
- Windows interaction checks: keyboard entry, edit/cancel/delete, scrolling/search,
  and month refresh. Report actual environment and limitations.
- Android device/emulator checks: document import, read-only UI, offline behavior,
  and invalid snapshot recovery. Desktop tests alone do not establish Android support.

Each implementation milestone records checks run and results in docs/PLAN.md.
