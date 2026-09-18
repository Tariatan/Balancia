# Balancia

A personal finance ledger for Windows, with a read-only Android companion.
Record income, expenses, and transfers; browse history; see account balances and
upcoming recurring payments. C#/.NET, Avalonia, and SQLite are the agreed stack.

**Status:** M1 Windows shell and test foundation. Navigation and empty states work;
account management, ledger operations, CSV import, reminders, and Android are not
implemented. The shell does not read the private export or create a live database.

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

Install .NET SDK **10.0.201**, pinned by global.json. All projects target net10.0.
Direct packages are pinned in project files; packages.lock.json files lock the
transitive graph. Use locked restore for ordinary builds; intentionally regenerate
and review locks when changing dependencies. Initial restore needs NuGet access.

```powershell
dotnet restore Balancia.slnx --locked-mode
dotnet build Balancia.slnx -c Release --no-restore
dotnet test Balancia.slnx -c Release --no-build --no-restore
dotnet run --project src/Balancia.Desktop -c Release --no-build --no-restore
```

Close the app before rebuilding on Windows to release its executable files.

| Dependency | Version |
| --- | --- |
| Avalonia.Desktop / Avalonia.Themes.Fluent | 12.1.2 |
| Microsoft.Data.Sqlite | 10.0.12 |
| Microsoft.NET.Test.Sdk | 18.10.1 |
| xUnit | 2.9.3 |
| xunit.runner.visualstudio | 4.0.0 |

Core has no package dependencies. Storage uses Microsoft.Data.Sqlite directly.
The initial shell uses small navigation event handlers; introduce view models when
editable state arrives in M2 rather than adding a framework to empty screens.
Tests use synthetic data and temporary SQLite files, removed after each test.
The SQLite connection factory is infrastructure only; it does not create a ledger
schema. Android workloads, device access, and packaging remain unchecked.

The harness explicitly instructs agents to read MEMORY.md; it does not assume
that filename is loaded automatically. The instruction entry point follows the
[official AGENTS.md guidance](https://learn.chatgpt.com/docs/agent-configuration/agents-md).
