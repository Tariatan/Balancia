using System.Globalization;
using System.Text;
using Balancia.Core;

namespace Balancia.Storage;

public sealed partial class LedgerStore
{
    public int ExportCsv(string exportPath)
    {
        var snapshot = ReadSnapshot();
        AtomicFileWriter.Write(exportPath, stream =>
        {
            using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true);
            WriteCsvRow(
                writer,
                "ID",
                "Date",
                "Type",
                "Description",
                "Amount",
                "Account",
                "DestinationAccount",
                "Category",
                "Memo");

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
            writer.Flush();
        });
        return snapshot.Accounts.Count + snapshot.Entries.Count;
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
