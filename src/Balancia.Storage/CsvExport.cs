using System.Globalization;
using System.Text;
using System.Text.Json;
using Balancia.Core;

namespace Balancia.Storage;

public sealed partial class LedgerStore
{
    public int ExportCsv(string exportPath) => LogOperation(() => ExportCsvCore(exportPath));

    private int ExportCsvCore(string exportPath)
    {
        using var connection = connections.Open();
        using var transaction = connection.BeginTransaction(deferred: true);
        var snapshot = ReadSnapshot(connection, transaction);
        var templates = new List<ReminderTemplate>();
        using (var command = Command(connection, transaction,
            "SELECT id,description,expected_date,indicative_amount,interval_months,archived FROM reminder_templates ORDER BY expected_date,id"))
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                templates.Add(new ReminderTemplate(reader.GetString(0), reader.GetString(1),
                    ParseDate(reader.GetString(2)), new Money(reader.GetInt64(3)), reader.GetInt32(4), reader.GetBoolean(5)));
            }
        }
        transaction.Commit();
        AtomicFileWriter.Write(exportPath, stream =>
        {
            using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true);
            WriteCsvRow(
                writer,
                CsvImportRowValidator.Header);

            foreach (var account in snapshot.Accounts)
            {
                WriteCsvRow(
                    writer,
                    "opening:" + account.Id,
                    account.OpeningDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    "OpeningBalance",
                    "Opening balance",
                    AmountCsv(account.OpeningAmount),
                    account.Name,
                    "",
                    "",
                    "");
            }

            foreach (var (id, draft, accountName, destinationName, categoryPath) in snapshot.Entries)
            {
                var signed = draft.Kind == TransactionKind.Expense ? -draft.Amount : draft.Amount;
                WriteCsvRow(
                    writer,
                    id,
                    draft.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    draft.Kind.ToString(),
                    draft.Description,
                    AmountCsv(signed),
                    accountName,
                    destinationName ?? "",
                    categoryPath ?? "",
                    draft.Memo);
            }
            foreach (var template in templates)
            {
                WriteCsvRow(writer, "reminder:" + template.Id, DateText(template.ExpectedDate), "Reminder",
                    template.Description, AmountCsv(template.IndicativeAmount), "", "", "",
                    JsonSerializer.Serialize(new CsvReminderOptions(template.IntervalMonths, template.Archived)));
            }
            writer.Flush();
        });
        return snapshot.Accounts.Count + snapshot.Entries.Count + templates.Count;
    }

    private static string AmountCsv(Money amount) => amount.Francs.ToString("0.00", CultureInfo.InvariantCulture);

    private static void WriteCsvRow(TextWriter writer, params string[] cells)
    {
        for (var i = 0; i < cells.Length; i++)
        {
            if (i > 0)
            {
                writer.Write(',');
            }

            writer.Write(CsvCell(cells[i]));
        }

        writer.WriteLine();
    }

    private static string CsvCell(string value) => value.IndexOfAny([',', '"', '\r', '\n']) >= 0
        ? "\"" + value.Replace("\"", "\"\"") + "\""
        : value;
}
