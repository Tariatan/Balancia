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
        if (trimmedName.Length == 0)
        {
            throw new ArgumentException("Enter an account name.");
        }

        if (openingDate > Today)
        {
            throw new ArgumentException("Opening date cannot be in the future.");
        }

        var key = id ?? Guid.NewGuid().ToString("N");
        var openingId = OpeningId(key);
        Write((c, tx) =>
        {
            if (id is not null)
            {
                RequireAccount(c, tx, id);
            }

            var earliest = LedgerSql.Scalar(c, tx, "SELECT MIN(l.date) FROM ledger l JOIN movements m ON m.transaction_id=l.id WHERE m.account_id=$id AND l.kind<>'OpeningBalance'", ("$id", key));
            if (earliest is string date && openingDate > LedgerSql.ParseDate(date))
            {
                throw new ArgumentException("Opening date must be on or before the first transaction in this account.");
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
        if (LedgerSql.Scalar(c, tx, "SELECT 1 FROM movements WHERE account_id=$id AND transaction_id<>$opening LIMIT 1", ("$id", id), ("$opening", openingId)) is not null)
        {
            throw new InvalidOperationException("An account with transactions cannot be deleted. Archive it instead.");
        }

        LedgerSql.Execute(c, tx, "UPDATE import_sources SET locally_modified=1,transaction_id=NULL WHERE transaction_id=$opening", ("$opening", openingId));
        LedgerSql.Execute(c, tx, "DELETE FROM movements WHERE transaction_id=$opening AND account_id=$id; DELETE FROM ledger WHERE id=$opening; DELETE FROM accounts WHERE id=$id", ("$id", id), ("$opening", openingId));
    });

    private static void RequireAccount(SqliteConnection c, SqliteTransaction tx, string id) =>
        LedgerSql.Require(c, tx, AccountExistsSql, id, "Account no longer exists.");

    private static List<Account> ReadAccountsWithBalances(SqliteConnection c, SqliteTransaction tx)
    {
        var balances = ReadAccountTotals(c, tx);
        var accounts = new List<Account>();
        using var cmd = LedgerSql.Command(c, tx, "SELECT a.id,a.name,a.opening_date,a.archived,m.amount FROM accounts a JOIN movements m ON m.transaction_id='opening:' || a.id AND m.account_id=a.id ORDER BY a.name COLLATE NOCASE");
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var id = r.GetString(0);
            accounts.Add(new Account(id, r.GetString(1), LedgerSql.ParseDate(r.GetString(2)), new Money(r.GetInt64(4)), r.GetBoolean(3), new Money((long)balances.GetValueOrDefault(id))));
        }

        return accounts;
    }
}
