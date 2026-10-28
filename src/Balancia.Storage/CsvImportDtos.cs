namespace Balancia.Storage;

public sealed record ImportIssue(long Line, string Message);
public sealed record ImportAccountTotal(string Account, long Centimes);
public sealed record ImportDateExample(long Line, DateOnly Date);
public sealed record ImportSummary(int Rows, int Expenses, int Incomes, int Transfers, int Openings,
    IReadOnlyList<ImportAccountTotal> AccountTotals, IReadOnlyList<string> Categories, int Reminders = 0);
/// <summary>What an import replaces: the accounts, transactions (opening balances excluded) and reminders the ledger holds now.</summary>
public sealed record ExistingLedger(int Accounts, int Transactions, int Reminders)
{
    public bool IsEmpty => Accounts == 0 && Transactions == 0 && Reminders == 0;
}

/// <summary><paramref name="Imported"/> is the number of entries the file added; <paramref name="RecoveryCopyPath"/> is the copy of the replaced ledger, or null when it held no data.</summary>
public sealed record ImportResult(int Imported, string? RecoveryCopyPath);

/// <summary>Member names are the CSV Type column values written by export and matched exactly by import; renaming one changes the file format.</summary>
internal enum CsvRowType
{
    Expense,
    Income,
    Transfer,
    OpeningBalance,
    Reminder,
}

internal sealed record CsvImportRow(long Line, string Id, DateOnly Date, string Description, long Amount,
    CsvRowType Type, string Category, string Account, string Memo, string DestinationAccount);
internal sealed record CsvImportGroup(string Id, CsvRowType Kind, CsvImportRow[] Rows);
