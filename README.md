# Balancia

A personal finance ledger for Windows, with a read-only Android companion.
Record income, expenses, and transfers; browse history; see account balances and
upcoming recurring payments. C#/.NET, Avalonia, and SQLite are the agreed stack.

**Status:** The Windows ledger, CSV import/export, recurring reminders, snapshot
backup/restore, and read-only Android snapshot viewer are implemented. Release
polish and distribution remain. Dashboard balances and history come from the
local SQLite ledger; Transactions provides combined search and filters.

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

The optional check does not replace importer or reconciliation tests.
No additional packages are required for these PowerShell checks.

## Private data

`transactions.csv` is the user's private import source. It is intentionally ignored
by Git. Keep live databases, exported snapshots, and backups outside source
control. Committed tests use only synthetic fixtures.
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
The Windows editor uses small navigation and dialog event handlers.
Tests use synthetic data and temporary SQLite files, removed after each test.
The app stores its working database at `%LOCALAPPDATA%\Balancia\balancia.db`.
For a separate test dataset, pass `--data-dir C:\absolute\directory` after `--`
in the `dotnet run` command. The app creates that directory and its version 3
schema on first run. Upgrading a version 1 database writes a consistent `.bak`
file beside it before migration. Opening an unsupported newer schema fails. Close the
running app before rebuilding on Windows. Android workloads, device access, and
packaging remain unchecked.

To import, open Transactions, choose **Import CSV**, inspect the preview,
and apply it. Invalid rows block import. Repeated identical imports add nothing;
changed CSV IDs or locally edited imported entries cause a conflict. The
source file is never modified. For a private-export reconciliation test in an
isolated temporary database, set `BALANCIA_PRIVATE_IMPORT_PATH` to its absolute
path before running the test command, then remove the environment variable.
**Export CSV** on the Transactions page saves the current ledger, including
opening balances, as a standard nine-column CSV. It is a ledger export and has
a different format from the eleven-column source accepted by Import CSV.

The Transactions page shows 100 newest entries at a time. Description search is
case-insensitive; account, type, category/subcategory, inclusive date, and
inclusive absolute CHF amount filters combine. A parent category includes its
subcategories. Use Previous/Next to browse results. The overview shows all
transactions in the selected period; double-clicking a row selects it in the
Transactions page. The window minimum is 1280 × 1280.
The top tabs are Overview, Transactions, and Categories. Account management is
available from the Accounts card on Overview. Recurring
template Add/Edit actions are in the Upcoming payments card on Overview.
Transactions also holds Export snapshot and Restore snapshot; Categories places
Add/Edit above a list that grows with the window.

The harness explicitly instructs agents to read MEMORY.md; it does not assume
that filename is loaded automatically. The instruction entry point follows the
[official AGENTS.md guidance](https://learn.chatgpt.com/docs/agent-configuration/agents-md).
