using System.Globalization;

namespace Balancia.Storage;

internal static class CsvImportRowValidator
{
    public static readonly string[] Header =
        ["ID", "Date", "Description", "Currency", "Amount", "Type", "Tags", "Account", "Status", "Memo", "IOU"];

    private const int IdIndex = 0;
    private const int DateIndex = 1;
    private const int DescriptionIndex = 2;
    private const int CurrencyIndex = 3;
    private const int AmountIndex = 4;
    private const int TypeIndex = 5;
    private const int TagsIndex = 6;
    private const int AccountIndex = 7;
    private const int StatusIndex = 8;
    private const int MemoIndex = 9;
    private const int IouIndex = 10;

    private static readonly CultureInfo DateCulture = CreateDateCulture();

    private static CultureInfo CreateDateCulture()
    {
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.DateTimeFormat.Calendar.TwoDigitYearMax = 2099;
        return culture;
    }

    public static (CsvImportRow? Row, List<string> Errors) Validate(string[] f, long line)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(f[IdIndex]))
        {
            errors.Add("Missing source ID.");
        }

        if (!DateOnly.TryParseExact(f[DateIndex], "dd-MM-yy", DateCulture, DateTimeStyles.None, out var date))
        {
            errors.Add("Date must be DD-MM-YY in 2000–2099.");
        }

        if (f[CurrencyIndex] != "CHF")
        {
            errors.Add("Only CHF currency is supported.");
        }

        long amount = 0;
        var amountValid = decimal.TryParse(f[AmountIndex], NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var parsed) &&
            parsed * 100 == decimal.Truncate(parsed * 100) &&
            parsed * 100 >= long.MinValue && parsed * 100 <= long.MaxValue;
        if (!amountValid)
        {
            errors.Add("Amount must be an exact CHF centime within Int64 range.");
        }
        else
        {
            amount = (long)(parsed * 100);
        }

        if (f[TypeIndex] is not ("Expense" or "Income" or "Transfer"))
        {
            errors.Add("Unsupported transaction type.");
        }

        if (amountValid && (f[TypeIndex] == "Expense" && amount >= 0 || f[TypeIndex] == "Income" && amount <= 0 || amount == 0))
        {
            errors.Add("Amount sign or zero conflicts with transaction type.");
        }

        if (string.IsNullOrWhiteSpace(f[AccountIndex]))
        {
            errors.Add("Account is required.");
        }

        if (f[StatusIndex] != "Cleared")
        {
            errors.Add("Unsupported status; review before import.");
        }

        if (!string.IsNullOrWhiteSpace(f[IouIndex]))
        {
            errors.Add("IOU data is unsupported; review before import.");
        }

        var tagParts = f[TagsIndex].Split('/');
        if (tagParts.Length > 2 || f[TagsIndex].Length > 0 && tagParts.Any(string.IsNullOrWhiteSpace))
        {
            errors.Add("Tags must be a standalone category or Parent / Child.");
        }

        var row = errors.Count == 0
            ? new CsvImportRow(line, f[IdIndex], date, f[DescriptionIndex], amount, f[TypeIndex], f[TagsIndex], f[AccountIndex], f[MemoIndex], f)
            : null;
        return (row, errors);
    }
}
