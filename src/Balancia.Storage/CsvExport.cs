using System.Globalization;
using System.Text;
using Balancia.Core;

namespace Balancia.Storage;

public sealed partial class LedgerStore
{
    public int ExportCsv(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);
        var snapshot = ReadSnapshot();
        var temporaryPath = fullPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true))
            {
                WriteCsvRow(writer, "ID", "Date", "Type", "Description", "Amount", "Account", "DestinationAccount", "Category", "Memo");
                foreach (var account in snapshot.Accounts)
                    WriteCsvRow(writer, "opening:" + account.Id, account.OpeningDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        "OpeningBalance", "Opening balance", AmountCsv(account.OpeningAmount), account.Name, "", "", "");
                foreach (var entry in snapshot.Entries)
                {
                    var draft = entry.Draft;
                    var signed = draft.Kind == TransactionKind.Expense ? new Money(checked(-draft.Amount.Centimes)) : draft.Amount;
                    WriteCsvRow(writer, entry.Id, draft.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        draft.Kind.ToString(), draft.Description, AmountCsv(signed), entry.AccountName,
                        entry.DestinationName ?? "", entry.CategoryPath ?? "", draft.Memo);
                }
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, fullPath, overwrite: true);
            return snapshot.Accounts.Count + snapshot.Entries.Count;
        }
        catch
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            throw;
        }
    }

    private static string AmountCsv(Money amount) => amount.Francs.ToString("0.00", CultureInfo.InvariantCulture);

    private static void WriteCsvRow(TextWriter writer, params string[] cells) =>
        writer.WriteLine(string.Join(",", cells.Select(CsvCell)));

    private static string CsvCell(string value) => value.IndexOfAny([',', '"', '\r', '\n']) >= 0
        ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
}
