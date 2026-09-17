# Implementation plan and checkpoint

Last updated: 2026-09-17.

## Milestones

| Milestone | Deliverable | Exit evidence | Status |
| --- | --- | --- | --- |
| M0 | Specification and context harness | Files, working local links, harness check, source unchanged | Complete |
| M1 | Pinned SDK/packages, solution, Windows shell, test projects | Restore/build/test and manual shell launch; exact commands in README | Not started |
| M2 | Accounts, categories, openings, ledger operations | L01–L07, SQLite atomicity tests, immediate UI refresh | Not started |
| M3 | Buxfer import preview and application | I01–I07, current local source reconciliation, repeated import no-op | Not started |
| M4 | Home, transaction form, history/search/filtering | H01–H05, keyboard/scroll checks, P01 measurement | Not started |
| M5 | Recurring templates and reminders | R01–R10, editable schedule and overdue behavior in UI | Not started |
| M6 | Backups and snapshot export | S01, S06; recovery tested before relying on migration | Not started |
| M7 | Android viewer and snapshot import | S02–S05 on device/emulator, packaging and refresh instructions | Not started |

Deliver working slices; do not build all infrastructure before displaying useful
data. M2 can start with a minimal editor, refined in M4. Windows usefulness precedes
Android polish. Keep the source CSV untouched during implementation.

## Current checkpoint

- Created AGENTS.md, MEMORY.md, README.md, specification, architecture, acceptance
  scenarios, this plan, ignore/editor defaults, and a PowerShell harness checker.
- Re-read current CSV type counts and SHA-256; former Refund is now Income.
- Observed installed .NET SDKs; package compatibility and Android tooling unchecked.
- Verified on 2026-09-17: `powershell -NoProfile -File scripts/Check-Harness.ps1`
  passed required-file, local Markdown link, and private-data ignore checks.
- Verified on 2026-09-17: the same command with `-CheckPrivateImport` passed and
  inspected 808 rows without printing financial values. Source SHA-256 remained
  unchanged from the start of this work. No application tests exist yet.
- Git initialized on main; private GitHub repository created at
  https://github.com/Tariatan/Balancia on 2026-09-17. Private CSV remains ignored.
- No application code, database, UI prototype, or CI exists.

## Next concrete action

Begin M1: check current .NET/Avalonia compatibility, select and pin dependencies,
create the smallest Windows shell plus Core/Storage test projects, and verify
restore/build/test/launch. Add build/run commands only after running them.

## Handoff format for subsequent work

Record date, milestone, behavior implemented, files affected, exact checks and
results, limitations, and next action. Update existing checkpoint facts rather than
appending a full transcript. Keep unfinished milestones visibly unfinished.
