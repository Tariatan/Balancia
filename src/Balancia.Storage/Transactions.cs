using Balancia.Core;
using Microsoft.Data.Sqlite;
using Serilog;

namespace Balancia.Storage;

public sealed partial class LedgerStore
{
    private const string EditableTransactionSql = "SELECT 1 FROM ledger WHERE id=$id AND kind<>'OpeningBalance'";
    private const string TransactionGoneMessage = "Transaction no longer exists.";

    public string SaveTransaction(string? id, TransactionDraft draft) => SaveTransactionCore(id, draft, null, false);

    public string SaveTransactionWithCategoryPath(string? id, TransactionDraft draft, string? categoryPath)
    {
        if (draft.CategoryId is not null)
        {
            throw new ArgumentException("Provide either a category path or a category ID, not both.");
        }

        if (draft.Kind == TransactionKind.Transfer && !string.IsNullOrWhiteSpace(categoryPath))
        {
            throw new ArgumentException("Transfers do not have expense categories.");
        }

        _ = ParseCategoryPath(categoryPath);
        return SaveTransactionCore(id, draft, categoryPath, true);
    }

    private string SaveTransactionCore(string? id, TransactionDraft draft, string? categoryPath, bool byPath)
    {
        try
        {
            draft.Validate(Today);
        }
        catch (ArgumentException exception)
        {
            Log.ForContext<LedgerStore>().Information(
                "Transaction validation rejected, Kind: '{Kind}', AccountId: '{AccountId}', Field: '{Field}', Reason: '{Reason}'",
                draft.Kind, draft.AccountId, exception.ParamName, exception.Message);
            throw;
        }

        // Reminders match the exact description, so stray surrounding spaces must not make a payment miss its reminder.
        draft = draft with { Description = draft.Description.Trim() };
        var key = id ?? Guid.NewGuid().ToString("N");
        Write((c, tx) =>
        {
            if (id is not null)
            {
                LedgerSql.Require(c, tx, EditableTransactionSql, id, TransactionGoneMessage);
            }

            var saved = id is null ? null : ReadSavedTransaction(c, tx, id);
            var accounts = ReadAccountsForTransaction(c, tx);
            var categories = ReadCategories(c, tx);
            var error = byPath
                ? TransactionRules.CheckSave(accounts, categories, draft.Date, draft.AccountId, draft.DestinationId, categoryPath, saved)
                : TransactionRules.CheckAccounts(accounts, draft.Date, draft.AccountId, draft.DestinationId, saved)
                    ?? (draft.CategoryId is null ? null : TransactionRules.CheckCategoryId(categories, draft.CategoryId, saved));
            if (error is not null)
            {
                throw new ArgumentException(error);
            }

            var categoryId = byPath ? TransactionRules.FindByPath(categories, categoryPath)?.Id : draft.CategoryId;
            LedgerSql.Execute(c, tx, """
                INSERT INTO ledger VALUES($id,$kind,$date,$description,$category,$memo)
                ON CONFLICT(id) DO UPDATE SET kind=excluded.kind,date=excluded.date,description=excluded.description,
                    category_id=excluded.category_id,memo=excluded.memo;
                DELETE FROM movements WHERE transaction_id=$id;
                """, ("$id", key), ("$kind", draft.Kind.ToString()), ("$date", LedgerSql.DateText(draft.Date)),
                ("$description", draft.Description), ("$category", categoryId), ("$memo", draft.Memo));
            AddMovement(c, tx, key, draft.AccountId, draft.Kind == TransactionKind.Income ? draft.Amount.Centimes : -draft.Amount.Centimes);
            if (draft.DestinationId is not null)
            {
                AddMovement(c, tx, key, draft.DestinationId, draft.Amount.Centimes);
            }
        });
        return key;
    }

    public void DeleteTransaction(string id) => Write((c, tx) =>
    {
        LedgerSql.Require(c, tx, EditableTransactionSql, id, TransactionGoneMessage);
        LedgerSql.Execute(c, tx, "DELETE FROM ledger WHERE id=$id", ("$id", id));
    });

    private static void ValidateAccount(SqliteConnection c, SqliteTransaction tx, string id, DateOnly date, string? transactionId)
    {
        var saved = transactionId is null ? null : ReadSavedTransaction(c, tx, transactionId);
        if (TransactionRules.CheckAccounts(ReadAccountsForTransaction(c, tx), date, id, null, saved) is { } message)
        {
            throw new ArgumentException(message);
        }
    }

    /// <summary>The accounts and the category the stored transaction already uses.</summary>
    private static SavedTransaction ReadSavedTransaction(SqliteConnection c, SqliteTransaction tx, string id)
    {
        var accountIds = new List<string>();
        using (var cmd = LedgerSql.Command(c, tx, "SELECT account_id FROM movements WHERE transaction_id=$id", ("$id", id)))
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                accountIds.Add(reader.GetString(0));
            }
        }

        return new SavedTransaction(accountIds, LedgerSql.Scalar(c, tx, "SELECT category_id FROM ledger WHERE id=$id", ("$id", id)) as string);
    }

    /// <summary>The accounts with what the transaction checks need; balances and first transaction dates are left out because they cost a pass over the ledger.</summary>
    private static List<Account> ReadAccountsForTransaction(SqliteConnection c, SqliteTransaction tx)
    {
        var accounts = new List<Account>();
        using var cmd = LedgerSql.Command(c, tx, "SELECT id,name,opening_date,archived FROM accounts");
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            accounts.Add(new Account(reader.GetString(0), reader.GetString(1), LedgerSql.ParseDate(reader.GetString(2)), Money.Zero, reader.GetBoolean(3), Money.Zero));
        }

        return accounts;
    }

    private static void AddMovement(SqliteConnection c, SqliteTransaction tx, string id, string account, long amount) =>
        LedgerSql.Execute(c, tx, "INSERT INTO movements VALUES($id,$account,$amount)", ("$id", id), ("$account", account), ("$amount", amount));
}
