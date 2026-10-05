# Balancia agent instructions

## Start here

Read [MEMORY.md](MEMORY.md), then
[docs/Technical_Design_Description.md](docs/Technical_Design_Description.md)
for product behavior and technical decisions, and
[docs/PLAN.md](docs/PLAN.md) for remaining/upcoming tasks. Follow the coding guidelines defined by the common harness.
Load other context only as needed.

The user's current instructions take precedence. The Technical Design
Description owns product behavior and technical decisions; memory is a
compact handoff, not a second specification. Proposals are not user-confirmed
requirements. Resolve contradictions explicitly instead of silently choosing
an old memory entry.

## Product boundaries

Balancia is a single-user, local personal finance ledger. Windows is the
only writer; Android is read-only. Use C#/.NET, Avalonia, and SQLite. No hosted
backend, bank integration, subscription, or cloud account system is required.

## Financial invariants

- Persist money as signed 64-bit integer centimes; use checked arithmetic and
  decimal parsing. Never use binary floating point for monetary calculations.
- A transfer is one editable transaction with two balanced account movements.
  Create, edit, and delete both movements atomically.
- Opening balances affect account balances and net worth.
- Recurrence matching uses exact description.
  Amount is ignored; earlier-month payments do not satisfy an occurrence.
- Never synchronize the live database file. Export consistent snapshots and
  validate a received snapshot before replacing the last usable Android copy.

## Work and verification

Implement one usable slice at a time from docs/PLAN.md. Keep domain logic free of
UI and platform APIs; keep SQLite queries parameterized. Use synthetic financial
data for committed tests, examples, screenshots, and logs.

Before changing code, identify the affected workflow in the Technical Design
Description. Test meaningful money, import, recurrence, persistence, and
snapshot failure behavior. UI changes need a manual interaction check when an
executable UI exists. Do not claim tests passed when the required
platform/tooling was unavailable.

Use README.md for the verified restore/build/test/run commands for Balancia.slnx.
Restore in locked mode. Keep global.json, package versions, and lock files aligned;
review lock changes when intentionally updating dependencies. Close the running
app before rebuilding. Never present planned commands as verified ones.

## Versioning

Always bump Balancia.Desktop's application version whenever any project changes,
even when Desktop's own implementation is unchanged. For other projects, bump
versions only when their own implementation changed.
Record notable implementation changes in [CHANGELOG.md](CHANGELOG.md) under the
affected project's version, newest first, with a date and the appropriate Keep
a Changelog category. Use Major for breaking behavior, Minor for compatible
features, and Patch for compatible bug fixes. Keep version metadata and changelog
entries aligned; planned rework belongs in PLAN.md until implemented.

## Editing Rules

- docs/PLAN.md is maintained exclusively by the user. Read it for context, but never modify it, including task status, completion notes, or formatting.
- Never add an empty line at the end of a file.
- If a file ends with an empty line, remove that empty line.
- Keep documentation links repository-relative. Do not publish machine-specific absolute paths or references to local agent libraries.

## Maintain context

Keep this file short and operational. Update
[docs/Technical_Design_Description.md](docs/Technical_Design_Description.md)
when behavior or architecture changes, then update docs/PLAN.md's task list.
Update local MEMORY.md with durable facts, evidence dates, limitations, and
the next concrete step when project context changes. Do not accumulate
transcripts, private financial values, or unverified claims. This concerns
repository memory only, not global agent memory.

At handoff, distinguish implemented, verified, planned, and blocked work. Record
checks actually run. Do not mark a milestone complete merely because documents
describe it. No automatic delegation, remote publication, or recurring automation
is established by this file.
