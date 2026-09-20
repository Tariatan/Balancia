# Balancia agent instructions

## Start here

Read [MEMORY.md](MEMORY.md), then [docs/SPECIFICATION.md](docs/SPECIFICATION.md).
For implementation, also read [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md), the
relevant scenarios in [docs/ACCEPTANCE.md](docs/ACCEPTANCE.md), and the current
checkpoint in [docs/PLAN.md](docs/PLAN.md). Follow
[docs/Coding Guidelines.md](docs/Coding Guidelines.md) for C# formatting. Load
other context only as needed.

The user's current instructions take precedence. The specification owns product
behavior; architecture owns technical decisions; memory is a compact handoff, not
a second specification. Proposals are not user-confirmed requirements. Resolve
contradictions explicitly instead of silently choosing an old memory entry.

## Product boundaries

Balancia is Slava's single-user, local personal finance ledger. Windows is the
only writer; Android is read-only. Use C#/.NET, Avalonia, and SQLite. No hosted
backend, bank integration, subscription, or cloud account system is required.
Keep the first implementation small; do not add frameworks, services, dependency
injection layers, or custom agent skills without a concrete need.

## Financial invariants

- Persist money as signed 64-bit integer centimes; use checked arithmetic and
  decimal parsing. Never use binary floating point for monetary calculations.
- A transfer is one editable transaction with two balanced account movements.
  Create, edit, and delete both movements atomically.
- Opening balances affect account balances and net worth, not income/expenses.
- Recurrence matching uses exact description and occurrence month AND year.
  Amount and day are ignored; earlier-month payments do not satisfy an occurrence.
- Re-read import files before relying on counts or balances from a previous run.
  Preserve source files. Do not discard unknown rows or change data silently.
- Never synchronize the live database file. Export consistent snapshots and
  validate a received snapshot before replacing the last usable Android copy.

## Work and verification

Implement one usable slice at a time from docs/PLAN.md. Keep domain logic free of
UI and platform APIs; keep SQLite queries parameterized. Use synthetic financial
data for committed tests, examples, screenshots, and logs. The root CSV is private
input, not a test fixture. Do not upload it or include its contents in commits.

Before changing code, identify the applicable acceptance scenarios. Test meaningful
money, import, recurrence, persistence, and snapshot failure behavior. UI changes
need a manual interaction check when an executable UI exists. Do not claim tests
passed when the required platform/tooling was unavailable.

Run `powershell -NoProfile -File scripts/Check-Harness.ps1` for harness changes.
Use README.md for the verified restore/build/test/run commands for Balancia.slnx.
Restore in locked mode. Keep global.json, package versions, and lock files aligned;
review lock changes when intentionally updating dependencies. Close the running
Windows app before rebuilding. Never present planned commands as verified ones.

## Maintain context

Keep this file short and operational. Update the owning document when behavior
or architecture changes, then update the checkpoint. Update local MEMORY.md with
durable facts, evidence dates, limitations, and the next concrete step when project
context changes. Do not accumulate transcripts, private financial values, or
unverified claims. This concerns repository memory only, not global agent memory.

At handoff, distinguish implemented, verified, planned, and blocked work. Record
checks actually run. Do not mark a milestone complete merely because documents
describe it. No automatic delegation, remote publication, or recurring automation
is established by this file.
