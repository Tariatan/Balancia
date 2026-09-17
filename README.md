# Balancia

A personal finance ledger for Windows, with a read-only Android companion.
Record income, expenses, and transfers; browse history; see account balances and
upcoming recurring payments. C#/.NET, Avalonia, and SQLite are the agreed stack.

**Status:** specification and project harness only. There is no application,
database, importer, Android package, or build pipeline yet.

## Project map

| File | Purpose |
| --- | --- |
| [AGENTS.md](AGENTS.md) | Agent entry point and working rules |
| [MEMORY.md](MEMORY.md) | Compact dated handoff and current evidence |
| [Specification](docs/SPECIFICATION.md) | Requirements, behavior, and explicit defaults |
| [Architecture](docs/ARCHITECTURE.md) | Technical boundaries and decision record |
| [Acceptance scenarios](docs/ACCEPTANCE.md) | Observable correctness and release checks |
| [Implementation plan](docs/PLAN.md) | Ordered milestones and current checkpoint |

## Harness check

From the project root, run:

```powershell
powershell -NoProfile -File scripts/Check-Harness.ps1
```

This checks required context files, local Markdown links, and ignore rules for
private data. It does not build or validate an application. It can optionally
check the current CSV structure without printing financial values:

```powershell
powershell -NoProfile -File scripts/Check-Harness.ps1 -CheckPrivateImport
```

The optional check does not replace the future importer or reconciliation tests.
No additional packages are required for these PowerShell checks.

## Private data

`transactions.csv` is the user's local Buxfer export. It is intentionally ignored
by Git. Keep live databases, exported snapshots, and backups outside source
control. Synthetic fixtures will live under tests once implementation starts.
Ignore rules do not encrypt data or remove files already tracked elsewhere.

## Development setup

SDKs observed on 2026-09-17: 8.0.302, 9.0.301, and 10.0.201. No SDK or Avalonia
package version is pinned yet. The first milestone will verify the framework
combination, pin it, and add executable build/test/run instructions. Android
workloads, device access, and packaging have not been checked.

The harness explicitly instructs agents to read MEMORY.md; it does not assume
that filename is loaded automatically. The instruction entry point follows the
[official AGENTS.md guidance](https://learn.chatgpt.com/docs/agent-configuration/agents-md).
