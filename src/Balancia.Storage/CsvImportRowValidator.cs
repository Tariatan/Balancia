using System.Globalization;
using System.Text.Json;

namespace Balancia.Storage;

internal static class CsvImportRowValidator
{
    public static readonly string[] Header =
        ["ID", "Date", "Type", "Description", "Amount", "Account", "DestinationAccount", "Category", "Memo"];

    private const int IdIndex = 0;
    private const int DateIndex = 1;
    private const int DescriptionIndex = 3;
    private const int AmountIndex = 4;
    private const int TypeIndex = 2;
    private const int CategoryIndex = 7;
    private const int AccountIndex = 5;
    private const int DestinationIndex = 6;
    private const int MemoIndex = 8;

    public static (CsvImportRow? Row, List<string> Errors) Validate(string[] f, long line)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(f[IdIndex]))
        {
            errors.Add("Missing source ID.");
        }

        if (!DateOnly.TryParseExact(f[DateIndex], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            errors.Add("Date must be YYYY-MM-DD.");
        }

        long amount = 0;
        var amountValid = decimal.TryParse(f[AmountIndex], NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var parsed) &&
            parsed >= long.MinValue / 100m && parsed <= long.MaxValue / 100m &&
            parsed * 100 == decimal.Truncate(parsed * 100);
        if (!amountValid)
        {
            errors.Add("Amount must be an exact CHF centime within Int64 range.");
        }
        else
        {
            amount = (long)(parsed * 100);
        }

        if (f[TypeIndex] == "Reminder")
        {
            if (!f[IdIndex].StartsWith("reminder:", StringComparison.Ordinal) || f[IdIndex].Length <= 9 ||
                string.IsNullOrWhiteSpace(f[DescriptionIndex]) || amount <= 0 ||
                f[AccountIndex].Length > 0 || f[DestinationIndex].Length > 0 || f[CategoryIndex].Length > 0)
            {
                errors.Add("Invalid reminder fields.");
            }

            try
            {
                var options = JsonSerializer.Deserialize<CsvReminderOptions>(f[MemoIndex]);
                using var metadata = JsonDocument.Parse(f[MemoIndex]);
                if (options is null || options.IntervalMonths <= 0 ||
                    !metadata.RootElement.TryGetProperty("Archived", out var archived) ||
                    archived.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    errors.Add("Invalid reminder schedule.");
                }
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException)
            {
                errors.Add("Invalid reminder schedule.");
            }

            return (errors.Count == 0
                ? new CsvImportRow(line, f[IdIndex], date, f[DescriptionIndex], amount, "Reminder", "", "", f[MemoIndex], f, "")
                : null, errors);
        }

        if (f[TypeIndex] is not ("Expense" or "Income" or "Transfer" or "OpeningBalance"))
        {
            errors.Add("Unsupported transaction type.");
        }

        if (amountValid && (f[TypeIndex] == "Expense" && (amount >= 0 || amount == long.MinValue) ||
            f[TypeIndex] is "Income" or "Transfer" && amount <= 0))
        {
            errors.Add("Amount sign or zero conflicts with transaction type.");
        }

        if (string.IsNullOrWhiteSpace(f[AccountIndex]))
        {
            errors.Add("Account is required.");
        }

        if (f[TypeIndex] == "Transfer" && (string.IsNullOrWhiteSpace(f[DestinationIndex]) ||
            string.Equals(f[AccountIndex], f[DestinationIndex], StringComparison.OrdinalIgnoreCase)))
        {
            errors.Add("A transfer requires two different accounts.");
        }
        else if (f[TypeIndex] != "Transfer" && f[DestinationIndex].Length > 0)
        {
            errors.Add("Only transfers may have a destination account.");
        }

        var categoryParts = f[CategoryIndex].Split('/');
        if (categoryParts.Length > 2 || f[CategoryIndex].Length > 0 && categoryParts.Any(string.IsNullOrWhiteSpace))
        {
            errors.Add("Category must be a standalone category or Parent / Child.");
        }

        if (f[TypeIndex] is "Transfer" or "OpeningBalance" && f[CategoryIndex].Length > 0)
        {
            errors.Add("Transfers and opening balances cannot have a category.");
        }

        var row = errors.Count == 0
            ? new CsvImportRow(line, f[IdIndex], date, f[DescriptionIndex], amount, f[TypeIndex], f[CategoryIndex], f[AccountIndex], f[MemoIndex], f, f[DestinationIndex])
            : null;
        return (row, errors);
    }
}
