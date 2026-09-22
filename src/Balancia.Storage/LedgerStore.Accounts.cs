using Balancia.Core;
using Microsoft.Data.Sqlite;

namespace Balancia.Storage;

public sealed partial class LedgerStore
{
    public string SaveAccount(string? id, string name, DateOnly openingDate, Money opening, bool archived = false)
    {
        ArgumentNullException.ThrowIfNull(name);
        name = name.Trim();
        if (name.Length == 0)
        {
            throw new ArgumentException("Enter an account name.");
        }

        if (openingDate > Today)
        {
            throw new ArgumentException("Opening date cannot be in the future.");
        }

        var key = id ?? Guid.NewGuid().ToString("N");
        Write((c, tx) =>
        {
            if (id is not null)
            {
                Require(c, tx, "SELECT 1 FROM accounts WHERE id=$id", id, "Account no longer exists.");
            }

            var earliest = Scalar(c, tx, "SELECT MIN(l.date) FROM ledger l JOIN movements m ON m.transaction_id=l.id WHERE m.account_id=$id AND l.kind<>'OpeningBalance'", ("$id", key));
            if (earliest is string date && openingDate > ParseDate(date))
            {
                throw new ArgumentException("Opening date must be on or before the first transaction in this account.");
            }

            Execute(c, tx, """
                INSERT INTO accounts VALUES($id,$name,$date,$archived)
                ON CONFLICT(id) DO UPDATE SET name=excluded.name, opening_date=excluded.opening_date, archived=excluded.archived;
                INSERT INTO ledger VALUES($opening,'OpeningBalance',$date,'Opening balance',NULL,'')
                ON CONFLICT(id) DO UPDATE SET date=excluded.date;
                INSERT INTO movements VALUES($opening,$id,$amount)
                ON CONFLICT(transaction_id,account_id) DO UPDATE SET amount=excluded.amount;
                """, ("$id", key), ("$name", name), ("$date", DateText(openingDate)),
                ("$archived", archived ? 1 : 0), ("$opening", "opening:" + key), ("$amount", opening.Centimes));
            if (id is not null)
            {
                Execute(c, tx, "UPDATE import_sources SET locally_modified=1 WHERE transaction_id=$id", ("$id", "opening:" + key));
            }
        });
        return key;
    }

    public void DeleteAccount(string id) => Write((c, tx) =>
    {
        Require(c, tx, "SELECT 1 FROM accounts WHERE id=$id", id, "Account no longer exists.");
        if (Scalar(c, tx, "SELECT 1 FROM movements WHERE account_id=$id AND transaction_id<>'opening:' || $id LIMIT 1", ("$id", id)) is not null)
        {
            throw new InvalidOperationException("An account with transactions cannot be deleted. Archive it instead.");
        }

        Execute(c, tx, "DELETE FROM movements WHERE account_id=$id AND transaction_id='opening:' || $id; DELETE FROM ledger WHERE id='opening:' || $id; DELETE FROM accounts WHERE id=$id", ("$id", id));
    });

    private static List<Account> ReadAccountsWithBalances(SqliteConnection c, SqliteTransaction tx)
    {
        var balances = new Dictionary<string, decimal>();
        using (var cmd = Command(c, tx, "SELECT account_id,amount FROM movements"))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
            {
                balances[r.GetString(0)] = balances.GetValueOrDefault(r.GetString(0)) + r.GetInt64(1);
            }
        }

        var accounts = new List<Account>();
        using (var cmd = Command(c, tx, "SELECT a.id,a.name,a.opening_date,a.archived,m.amount FROM accounts a JOIN movements m ON m.transaction_id='opening:' || a.id AND m.account_id=a.id ORDER BY a.name COLLATE NOCASE"))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
            {
                accounts.Add(new Account(r.GetString(0), r.GetString(1), ParseDate(r.GetString(2)), new Money(r.GetInt64(4)), r.GetBoolean(3), new Money(checked((long)balances.GetValueOrDefault(r.GetString(0))))));
            }
        }

        return accounts;
    }
}
