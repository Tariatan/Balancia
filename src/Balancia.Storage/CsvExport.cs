using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Balancia.Core;

namespace Balancia.Storage;

public sealed partial class LedgerStore
{
    private static readonly SearchValues<char> CsvSpecialChars = SearchValues.Create(",\"\r\n");

    public int ExportCsv(string exportPath) => LogOperation(() => ExportCsvCore(exportPath));

    private int ExportCsvCore(string exportPath)
    {
        LedgerSnapshot snapshot;
        List<ReminderTemplate> templates = [];
        using (var connection = connections.Open())
        using (var transaction = connection.BeginTransaction(deferred: true))
        {
            snapshot = ReadSnapshot(connection, transaction);
            using (var command = LedgerSql.Command(connection, transaction,
                "SELECT id,description,expected_date,indicative_amount,interval_months,archived FROM reminder_templates ORDER BY expected_date,id"))
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    templates.Add(ReadTemplate(reader));
                }
            }

            transaction.Commit();
        }

        AtomicFileWriter.Write(exportPath, stream =>
        {
            using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true);
            WriteCsvRow(writer, CsvImportRowValidator.Header);
            WriteAccountRows(writer, snapshot.Accounts);
            WriteEntryRows(writer, snapshot.Entries);
            WriteReminderRows(writer, templates);
        });
        return snapshot.Accounts.Count + snapshot.Entries.Count + templates.Count;
    }

    private static void WriteAccountRows(TextWriter writer, IEnumerable<Account> accounts)
    {
        foreach (var account in accounts)
        {
            WriteCsvRow(writer, OpeningId(account.Id), LedgerSql.DateText(account.OpeningDate), nameof(CsvRowType.OpeningBalance), "Opening balance",
                AmountCsv(account.OpeningAmount), account.Name, "", "", "");
        }
    }

    private static void WriteEntryRows(TextWriter writer, IEnumerable<LedgerEntry> entries)
    {
        foreach (var (id, draft, accountName, destinationName, categoryPath) in entries)
        {
            var signed = draft.Kind == TransactionKind.Expense ? -draft.Amount : draft.Amount;
            WriteCsvRow(writer, id, LedgerSql.DateText(draft.Date), draft.Kind.ToString(), draft.Description, AmountCsv(signed),
                accountName, destinationName ?? "", categoryPath ?? "", draft.Memo);
        }
    }

    private static void WriteReminderRows(TextWriter writer, IEnumerable<ReminderTemplate> templates)
    {
        foreach (var template in templates)
        {
            WriteCsvRow(writer, ReminderSourceId(template.Id), LedgerSql.DateText(template.ExpectedDate), nameof(CsvRowType.Reminder), template.Description,
                AmountCsv(template.IndicativeAmount), "", "", "",
                JsonSerializer.Serialize(new CsvReminderOptions(template.IntervalMonths, template.Archived)));
        }
    }

    private static string AmountCsv(Money amount) => amount.Francs.ToString("0.00", CultureInfo.InvariantCulture);

    private static void WriteCsvRow(TextWriter writer, params ReadOnlySpan<string> cells)
    {
        for (var i = 0; i < cells.Length; i++)
        {
            if (i > 0)
            {
                writer.Write(',');
            }

            WriteCsvCell(writer, cells[i]);
        }

        writer.WriteLine();
    }

    private static void WriteCsvCell(TextWriter writer, string value)
    {
        if (value.AsSpan().IndexOfAny(CsvSpecialChars) < 0)
        {
            writer.Write(value);
            return;
        }

        writer.Write('"');
        writer.Write(value.Replace("\"", "\"\""));
        writer.Write('"');
    }
}
