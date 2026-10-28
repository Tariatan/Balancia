using System.Globalization;
using Balancia.Core;
using Microsoft.Data.Sqlite;

namespace Balancia.Storage;

public sealed partial class LedgerStore
{
    private const string MarkReminderModifiedSql = "UPDATE import_sources SET locally_modified=1 WHERE external_id=$external";

    /// <summary>Returns each active reminder at its first occurrence not yet matched by a ledger entry, soonest first.</summary>
    public IReadOnlyList<Reminder> ReadReminders(DateOnly? asOf = null)
    {
        var today = asOf ?? Today;
        return ReadDeferred((c, tx) =>
        {
            var templates = ReadActiveTemplates(c, tx);
            var paidMonths = ReadPaidMonths(c, tx);
            var result = new List<Reminder>();
            foreach (var template in templates)
            {
                var paid = paidMonths.GetValueOrDefault(template.Description);
                result.Add(ReminderSchedule.Resolve(template, today, date => paid?.Contains(MonthText(date)) == true));
            }

            return (IReadOnlyList<Reminder>)[.. result.OrderBy(x => x.Occurrence)];
        });
    }

    private static List<ReminderTemplate> ReadActiveTemplates(SqliteConnection c, SqliteTransaction tx)
    {
        var templates = new List<ReminderTemplate>();
        using var cmd = LedgerSql.Command(c, tx, "SELECT id,description,expected_date,indicative_amount,interval_months,archived FROM reminder_templates WHERE archived=0 ORDER BY expected_date,description COLLATE NOCASE");
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            templates.Add(ReadTemplate(r));
        }

        return templates;
    }

    /// <summary>The calendar months (yyyy-MM) with a posted entry for each active reminder description, found in one pass over the ledger.</summary>
    private static Dictionary<string, HashSet<string>> ReadPaidMonths(SqliteConnection c, SqliteTransaction tx)
    {
        var months = new Dictionary<string, HashSet<string>>();
        using var cmd = LedgerSql.Command(c, tx, """
            SELECT DISTINCT description,substr(date,1,7) FROM ledger
            WHERE kind<>'OpeningBalance'
              AND description IN (SELECT description FROM reminder_templates WHERE archived=0)
            """);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (!months.TryGetValue(reader.GetString(0), out var set))
            {
                months[reader.GetString(0)] = set = [];
            }

            set.Add(reader.GetString(1));
        }

        return months;
    }

    private static string MonthText(DateOnly date) => date.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    private static ReminderTemplate ReadTemplate(SqliteDataReader r) =>
        new(r.GetString(0), r.GetString(1), LedgerSql.ParseDate(r.GetString(2)), new Money(r.GetInt64(3)), r.GetInt32(4), r.GetBoolean(5));

    public string SaveReminder(string? id, string description, DateOnly expectedDate, Money indicativeAmount, int intervalMonths, bool archived = false)
    {
        ArgumentNullException.ThrowIfNull(description);
        description = description.Trim();
        var key = id ?? Guid.NewGuid().ToString("N");
        Write((c, tx) =>
        {
            if (ReminderRules.CheckSave(ReadActiveTemplates(c, tx), key, description, indicativeAmount, intervalMonths, archived) is { } message)
            {
                throw new ArgumentException(message);
            }

            LedgerSql.Execute(c, tx, MarkReminderModifiedSql, ("$external", ReminderSourceId(key)));
            LedgerSql.Execute(c, tx, "INSERT INTO reminder_templates VALUES($id,$description,$date,$amount,$interval,$archived) ON CONFLICT(id) DO UPDATE SET description=excluded.description,expected_date=excluded.expected_date,indicative_amount=excluded.indicative_amount,interval_months=excluded.interval_months,archived=excluded.archived", ("$id", key), ("$description", description), ("$date", LedgerSql.DateText(expectedDate)), ("$amount", indicativeAmount.Centimes), ("$interval", intervalMonths), ("$archived", archived ? 1 : 0));
        });
        return key;
    }

    public void DeleteReminder(string id) => Write((c, tx) =>
    {
        LedgerSql.Execute(c, tx, MarkReminderModifiedSql, ("$external", ReminderSourceId(id)));
        LedgerSql.Execute(c, tx, "DELETE FROM reminder_templates WHERE id=$id", ("$id", id));
    });
}
