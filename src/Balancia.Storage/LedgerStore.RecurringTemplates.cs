using System.Globalization;
using Balancia.Core;
using Microsoft.Data.Sqlite;

namespace Balancia.Storage;

public sealed partial class LedgerStore
{
    public IReadOnlyList<RecurringReminder> ReadRecurringReminders(DateOnly? asOf = null)
    {
        var today = asOf ?? Today;
        using var c = connections.Open();
        using var tx = c.BeginTransaction(deferred: true);
        var result = new List<RecurringReminder>();
        using var cmd = Command(c, tx, "SELECT id,description,expected_date,indicative_amount,interval_months,archived FROM recurring_templates WHERE archived=0 ORDER BY expected_date,description COLLATE NOCASE");
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var template = new RecurringTemplate(r.GetString(0), r.GetString(1), ParseDate(r.GetString(2)), new Money(r.GetInt64(3)), r.GetInt32(4), r.GetBoolean(5));
            var occurrence = template.ExpectedDate;
            while (occurrence < today && IsSatisfied(c, tx, template.Description, occurrence))
            {
                occurrence = AddMonthsClamped(occurrence, template.IntervalMonths, template.ExpectedDate.Day);
            }

            result.Add(new RecurringReminder(template, occurrence, occurrence < today, IsSatisfied(c, tx, template.Description, occurrence)));
        }
        tx.Commit();
        return result.OrderBy(x => x.Satisfied ? 1 : 0).ThenBy(x => x.Occurrence).ToArray();
    }

    public string SaveRecurringTemplate(string? id, string description, DateOnly expectedDate, Money indicativeAmount, int intervalMonths, bool archived = false)
    {
        ArgumentNullException.ThrowIfNull(description);
        description = description.Trim();
        if (description.Length == 0)
        {
            throw new ArgumentException("Enter a description.");
        }

        if (indicativeAmount <= Money.Zero)
        {
            throw new ArgumentException("Enter amount greater than zero.");
        }

        if (intervalMonths <= 0)
        {
            throw new ArgumentException("Repeat interval must be positive.");
        }

        var key = id ?? Guid.NewGuid().ToString("N");
        Write((c, tx) =>
        {
            if (!archived && Scalar(c, tx, "SELECT 1 FROM recurring_templates WHERE archived=0 AND lower(description)=lower($description) AND id<>$id LIMIT 1", ("$description", description), ("$id", key)) is not null)
            {
                throw new ArgumentException("An active recurring template already uses this description.");
            }

            Execute(c, tx, "INSERT INTO recurring_templates VALUES($id,$description,$date,$amount,$interval,$archived) ON CONFLICT(id) DO UPDATE SET description=excluded.description,expected_date=excluded.expected_date,indicative_amount=excluded.indicative_amount,interval_months=excluded.interval_months,archived=excluded.archived", ("$id", key), ("$description", description), ("$date", DateText(expectedDate)), ("$amount", indicativeAmount.Centimes), ("$interval", intervalMonths), ("$archived", archived ? 1 : 0));
        });
        return key;
    }

    public void DeleteRecurringTemplate(string id) => Write((c, tx) => Execute(c, tx, "DELETE FROM recurring_templates WHERE id=$id", ("$id", id)));

    private static bool IsSatisfied(SqliteConnection c, SqliteTransaction tx, string description, DateOnly occurrence) =>
        Scalar(c, tx, "SELECT 1 FROM ledger WHERE kind<>'OpeningBalance' AND description=$description AND substr(date,1,7)=$month LIMIT 1", ("$description", description), ("$month", occurrence.ToString("yyyy-MM", CultureInfo.InvariantCulture))) is not null;

    private static DateOnly AddMonthsClamped(DateOnly date, int months, int desiredDay)
    {
        var first = new DateOnly(date.Year, date.Month, 1).AddMonths(months);
        return new DateOnly(first.Year, first.Month, Math.Min(desiredDay, DateTime.DaysInMonth(first.Year, first.Month)));
    }
}
