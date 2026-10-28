using System.Globalization;
using System.Text.Json;
using Balancia.Core;

namespace Balancia.Storage;

internal static class CsvImportRowValidator
{
    public static readonly string[] Header =
        ["ID", "Date", "Type", "Description", "Amount", "Account", "DestinationAccount", "Category", "Memo"];

    private const int IdIndex = 0;
    private const int DateIndex = 1;
    private const int TypeIndex = 2;
    private const int DescriptionIndex = 3;
    private const int AmountIndex = 4;
    private const int AccountIndex = 5;
    private const int DestinationIndex = 6;
    private const int CategoryIndex = 7;
    private const int MemoIndex = 8;

    public static (CsvImportRow? Row, List<string> Errors) Validate(string[] fields, long line, DateOnly today)
    {
        var id = fields[IdIndex];
        var description = fields[DescriptionIndex];
        var account = fields[AccountIndex];
        var destination = fields[DestinationIndex];
        var category = fields[CategoryIndex];
        var memo = fields[MemoIndex];
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(id))
        {
            errors.Add("Missing source ID.");
        }

        if (!DateOnly.TryParseExact(fields[DateIndex], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            errors.Add("Date must be YYYY-MM-DD.");
        }

        var amountValid = TryParseCentimes(fields[AmountIndex], out var amount);
        if (!amountValid)
        {
            errors.Add("Amount must be an exact CHF centime within Int64 range.");
        }

        CsvRowType? type = ParseType(fields[TypeIndex]);

        if (type == CsvRowType.Reminder)
        {
            if (!id.StartsWith(LedgerStore.ReminderIdPrefix, StringComparison.Ordinal) || id.Length <= LedgerStore.ReminderIdPrefix.Length ||
                account.Length > 0 || destination.Length > 0 || category.Length > 0)
            {
                errors.Add("Invalid reminder fields.");
            }

            AddIfNotNull(errors, ReminderRules.CheckDescription(description));
            if (amountValid)
            {
                AddIfNotNull(errors, ReminderRules.CheckAmount(new Money(amount)));
            }

            if (TryReadReminderSchedule(memo, out var intervalMonths))
            {
                AddIfNotNull(errors, ReminderRules.CheckInterval(intervalMonths));
            }
            else
            {
                errors.Add("Invalid reminder schedule.");
            }

            return (errors.Count == 0
                ? new CsvImportRow(line, id, date, description, amount, CsvRowType.Reminder, string.Empty, string.Empty, memo, string.Empty)
                : null, errors);
        }

        if (type is null)
        {
            errors.Add("Unsupported transaction type.");
        }

        if (date > today)
        {
            errors.Add("Date cannot be in the future.");
        }

        if (amountValid && (type == CsvRowType.Expense && amount is >= 0 or long.MinValue ||
            type is CsvRowType.Income or CsvRowType.Transfer && amount <= 0))
        {
            errors.Add("Amount sign or zero conflicts with transaction type.");
        }

        if (string.IsNullOrWhiteSpace(account))
        {
            errors.Add("Account is required.");
        }

        if (type == CsvRowType.Transfer && (string.IsNullOrWhiteSpace(destination) ||
            string.Equals(account, destination, StringComparison.OrdinalIgnoreCase)))
        {
            errors.Add("A transfer requires two different accounts.");
        }
        else if (type != CsvRowType.Transfer && destination.Length > 0)
        {
            errors.Add("Only transfers may have a destination account.");
        }

        if (category.Length > 0 && !(CategoryRules.TryParsePath(category, out var categoryNames) && categoryNames.Length > 0))
        {
            errors.Add(CategoryRules.InvalidPathMessage);
        }

        if (type is CsvRowType.Transfer or CsvRowType.OpeningBalance && category.Length > 0)
        {
            errors.Add("Transfers and opening balances cannot have a category.");
        }

        var row = errors.Count == 0
            ? new CsvImportRow(line, id, date, description, amount, type!.Value, category, account, memo, destination)
            : null;
        return (row, errors);
    }

    /// <summary>Exact, case-sensitive member name only; <c>Enum.TryParse</c> would also accept numbers, comma lists and padded text.</summary>
    private static CsvRowType? ParseType(string text) =>
        Enum.GetNames<CsvRowType>().Contains(text, StringComparer.Ordinal) ? Enum.Parse<CsvRowType>(text) : null;

    private static bool TryParseCentimes(string text, out long centimes)
    {
        centimes = 0;
        if (!decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var francs) ||
            francs < long.MinValue / 100m || francs > long.MaxValue / 100m)
        {
            return false;
        }

        var scaled = francs * 100;
        if (scaled != decimal.Truncate(scaled))
        {
            return false;
        }

        centimes = (long)scaled;
        return true;
    }

    private static void AddIfNotNull(List<string> errors, string? error)
    {
        if (error is not null)
        {
            errors.Add(error);
        }
    }

    /// <summary>Reads the repeat interval from the reminder memo; false when the memo is not a JSON object with a numeric interval and a boolean archived flag.</summary>
    private static bool TryReadReminderSchedule(string memo, out int intervalMonths)
    {
        intervalMonths = 0;
        try
        {
            using var metadata = JsonDocument.Parse(memo);
            var root = metadata.RootElement;
            return root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty(nameof(CsvReminderOptions.IntervalMonths), out var interval) &&
                interval.ValueKind == JsonValueKind.Number && interval.TryGetInt32(out intervalMonths) &&
                root.TryGetProperty(nameof(CsvReminderOptions.Archived), out var archived) &&
                archived.ValueKind is JsonValueKind.True or JsonValueKind.False;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
