namespace Balancia.Storage;

public sealed record ImportIssue(long Line, string Message);
public sealed record ImportAccountTotal(string Account, long Centimes);
public sealed record ImportDateExample(long Line, DateOnly Date);
public sealed record ImportSummary(int Rows, int Expenses, int Incomes, int Transfers, int Openings,
    IReadOnlyList<ImportAccountTotal> AccountTotals, IReadOnlyList<string> Categories, int Reminders = 0);
public sealed record ImportResult(int Added, int Unchanged);

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
    CsvRowType Type, string Category, string Account, string Memo, string[] Fields, string DestinationAccount);
internal sealed record CsvImportGroup(string Id, CsvRowType Kind, CsvImportRow[] Rows, string Fingerprint);
