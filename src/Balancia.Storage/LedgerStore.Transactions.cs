using Balancia.Core;
using Microsoft.Data.Sqlite;

namespace Balancia.Storage;

public sealed partial class LedgerStore
{
    public string SaveTransaction(string? id, TransactionDraft draft) => SaveTransactionCore(id, draft, null);

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

        var parts = ParseCategoryPath(categoryPath);
        return SaveTransactionCore(id, draft, parts);
    }

    private string SaveTransactionCore(string? id, TransactionDraft draft, string[]? categoryParts)
    {
        draft.Validate(Today);
        var key = id ?? Guid.NewGuid().ToString("N");
        Write((c, tx) =>
        {
            if (id is not null)
            {
                Require(c, tx, "SELECT 1 FROM ledger WHERE id=$id AND kind<>'OpeningBalance'", id, "Transaction no longer exists.");
            }

            ValidateAccount(c, tx, draft.AccountId, draft.Date, id);
            if (draft.DestinationId is not null)
            {
                ValidateAccount(c, tx, draft.DestinationId, draft.Date, id);
            }

            if (categoryParts is not null)
            {
                draft = draft with
                {
                    CategoryId = ResolveCategoryPath(c, tx, id, categoryParts)
                };
            }

            if (draft.CategoryId is not null)
            {
                Require(c, tx, "SELECT 1 FROM categories WHERE id=$id", draft.CategoryId, "Category no longer exists.");
                var archived = Convert.ToInt64(Scalar(c, tx, "SELECT archived FROM categories WHERE id=$id", ("$id", draft.CategoryId))) != 0;
                var previousCategory = id is null ? null : Scalar(c, tx, "SELECT category_id FROM ledger WHERE id=$id", ("$id", id)) as string;
                if (archived && previousCategory != draft.CategoryId)
                {
                    throw new ArgumentException("Choose an active category.");
                }
            }
            Execute(c, tx, """
                INSERT INTO ledger VALUES($id,$kind,$date,$description,$category,$memo)
                ON CONFLICT(id) DO UPDATE SET kind=excluded.kind,date=excluded.date,description=excluded.description,
                    category_id=excluded.category_id,memo=excluded.memo;
                DELETE FROM movements WHERE transaction_id=$id;
                """, ("$id", key), ("$kind", draft.Kind.ToString()), ("$date", DateText(draft.Date)),
                ("$description", draft.Description), ("$category", draft.CategoryId), ("$memo", draft.Memo));
            AddMovement(c, tx, key, draft.AccountId, draft.Kind == TransactionKind.Income ? draft.Amount.Centimes : -draft.Amount.Centimes);
            if (draft.DestinationId is not null)
            {
                AddMovement(c, tx, key, draft.DestinationId, draft.Amount.Centimes);
            }

            if (id is not null)
            {
                Execute(c, tx, "UPDATE import_sources SET locally_modified=1 WHERE transaction_id=$id", ("$id", id));
            }
        });
        return key;
    }

    public void DeleteTransaction(string id) => Write((c, tx) =>
    {
        Require(c, tx, "SELECT 1 FROM ledger WHERE id=$id AND kind<>'OpeningBalance'", id, "Transaction no longer exists.");
        Execute(c, tx, "UPDATE import_sources SET locally_modified=1,transaction_id=NULL WHERE transaction_id=$id", ("$id", id));
        Execute(c, tx, "DELETE FROM ledger WHERE id=$id", ("$id", id));
    });

    private static void ValidateAccount(SqliteConnection c, SqliteTransaction tx, string id, DateOnly date, string? transactionId)
    {
        Require(c, tx, "SELECT 1 FROM accounts WHERE id=$id", id, "Account no longer exists.");
        var opening = (string)Scalar(c, tx, "SELECT opening_date FROM accounts WHERE id=$id", ("$id", id))!;
        if (date < ParseDate(opening))
        {
            throw new ArgumentException("Transaction date precedes the account's opening date.");
        }

        var archived = Convert.ToInt64(Scalar(c, tx, "SELECT archived FROM accounts WHERE id=$id", ("$id", id))) != 0;
        if (archived && (transactionId is null || Scalar(c, tx, "SELECT 1 FROM movements WHERE transaction_id=$transaction AND account_id=$account", ("$transaction", transactionId), ("$account", id)) is null))
        {
            throw new ArgumentException("Choose an active account for new movements.");
        }
    }

    private static void AddMovement(SqliteConnection c, SqliteTransaction tx, string id, string account, long amount) =>
        Execute(c, tx, "INSERT INTO movements VALUES($id,$account,$amount)", ("$id", id), ("$account", account), ("$amount", amount));
}
