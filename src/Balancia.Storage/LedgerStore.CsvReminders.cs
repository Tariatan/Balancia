using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Balancia.Storage;

public sealed partial class LedgerStore
{
    private static void ImportCsvReminder(SqliteConnection connection, SqliteTransaction transaction, CsvImportRow row)
    {
        var id = row.Id[9..];
        var options = JsonSerializer.Deserialize<CsvReminderOptions>(row.Memo)!;
        using (var command = Command(connection, transaction,
            "SELECT description,expected_date,indicative_amount,interval_months,archived FROM reminder_templates WHERE id=$id", ("$id", id)))
        using (var reader = command.ExecuteReader())
        {
            if (reader.Read())
            {
                if (reader.GetString(0) != row.Description || ParseDate(reader.GetString(1)) != row.Date ||
                    reader.GetInt64(2) != row.Amount || reader.GetInt32(3) != options.IntervalMonths ||
                    reader.GetBoolean(4) != options.Archived)
                {
                    throw new InvalidOperationException("Imported reminder conflicts with existing reminder data.");
                }

                return;
            }
        }

        if (!options.Archived && Scalar(connection, transaction,
            "SELECT 1 FROM reminder_templates WHERE archived=0 AND lower(description)=lower($description) LIMIT 1",
            ("$description", row.Description.Trim())) is not null)
        {
            throw new InvalidOperationException("An active reminder already uses this description.");
        }

        Execute(connection, transaction, "INSERT INTO reminder_templates VALUES($id,$description,$date,$amount,$interval,$archived)",
            ("$id", id), ("$description", row.Description.Trim()), ("$date", DateText(row.Date)),
            ("$amount", row.Amount), ("$interval", options.IntervalMonths), ("$archived", options.Archived ? 1 : 0));
    }
}
