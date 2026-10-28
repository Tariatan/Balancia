using Balancia.Core;
using Microsoft.Data.Sqlite;

namespace Balancia.Storage;

public sealed partial class LedgerStore
{
    private const string AccountExistsSql = "SELECT 1 FROM accounts WHERE id=$id";

    /// <summary>Id of the ledger entry that carries an account's opening balance.</summary>
    internal static string OpeningId(string accountId) => "opening:" + accountId;

    public string SaveAccount(string? id, string name, DateOnly openingDate, Money opening, bool archived = false)
    {
        ArgumentNullException.ThrowIfNull(name);
        var trimmedName = name.Trim();
        var key = id ?? Guid.NewGuid().ToString("N");
        var openingId = OpeningId(key);
        Write((c, tx) =>
        {
            if (id is not null)
            {
                RequireAccount(c, tx, id);
            }

            if (AccountRules.CheckSave(ReadAccountsWithBalances(c, tx), id, trimmedName, openingDate, Today) is { } message)
            {
                throw new ArgumentException(message);
            }

            LedgerSql.Execute(c, tx, """
                INSERT INTO accounts VALUES($id,$name,$date,$archived)
                ON CONFLICT(id) DO UPDATE SET name=excluded.name, opening_date=excluded.opening_date, archived=excluded.archived;
                INSERT INTO ledger VALUES($opening,'OpeningBalance',$date,'Opening balance',NULL,'')
                ON CONFLICT(id) DO UPDATE SET date=excluded.date;
                INSERT INTO movements VALUES($opening,$id,$amount)
                ON CONFLICT(transaction_id,account_id) DO UPDATE SET amount=excluded.amount;
                """, ("$id", key), ("$name", trimmedName), ("$date", LedgerSql.DateText(openingDate)),
                ("$archived", archived ? 1 : 0), ("$opening", openingId), ("$amount", opening.Centimes));
            if (id is not null)
            {
                LedgerSql.Execute(c, tx, "UPDATE import_sources SET locally_modified=1 WHERE transaction_id=$id", ("$id", openingId));
            }
        });
        return key;
    }

    public void DeleteAccount(string id) => Write((c, tx) =>
    {
        RequireAccount(c, tx, id);
        var openingId = OpeningId(id);
        if (AccountRules.CheckDelete(ReadAccountsWithBalances(c, tx), id) is { } message)
        {
            throw new InvalidOperationException(message);
        }

        LedgerSql.Execute(c, tx, "UPDATE import_sources SET locally_modified=1,transaction_id=NULL WHERE transaction_id=$opening", ("$opening", openingId));
        LedgerSql.Execute(c, tx, "DELETE FROM movements WHERE transaction_id=$opening AND account_id=$id; DELETE FROM ledger WHERE id=$opening; DELETE FROM accounts WHERE id=$id", ("$id", id), ("$opening", openingId));
    });

    private static void RequireAccount(SqliteConnection c, SqliteTransaction tx, string id) =>
        LedgerSql.Require(c, tx, AccountExistsSql, id, "Account no longer exists.");

    private static List<Account> ReadAccountsWithBalances(SqliteConnection c, SqliteTransaction tx)
    {
        var balances = ReadAccountTotals(c, tx);
        var firstTransactions = ReadFirstTransactionDates(c, tx);
        var accounts = new List<Account>();
        using var cmd = LedgerSql.Command(c, tx, "SELECT a.id,a.name,a.opening_date,a.archived,m.amount FROM accounts a JOIN movements m ON m.transaction_id='opening:' || a.id AND m.account_id=a.id ORDER BY a.name COLLATE NOCASE");
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var id = r.GetString(0);
            accounts.Add(new Account(id, r.GetString(1), LedgerSql.ParseDate(r.GetString(2)), new Money(r.GetInt64(4)), r.GetBoolean(3),
                new Money((long)balances.GetValueOrDefault(id)), firstTransactions.TryGetValue(id, out var first) ? first : null));
        }

        return accounts;
    }

    /// <summary>The date of the earliest entry other than the opening balance on each account that has one.</summary>
    private static Dictionary<string, DateOnly> ReadFirstTransactionDates(SqliteConnection c, SqliteTransaction tx)
    {
        var dates = new Dictionary<string, DateOnly>();
        using var cmd = LedgerSql.Command(c, tx, "SELECT m.account_id,MIN(l.date) FROM movements m JOIN ledger l ON l.id=m.transaction_id WHERE l.kind<>'OpeningBalance' GROUP BY m.account_id");
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            dates[reader.GetString(0)] = LedgerSql.ParseDate(reader.GetString(1));
        }

        return dates;
    }
}
