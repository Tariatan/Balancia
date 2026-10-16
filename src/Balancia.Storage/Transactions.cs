using Balancia.Core;
using Microsoft.Data.Sqlite;
using Serilog;

namespace Balancia.Storage;

public sealed partial class LedgerStore
{
    private const string EditableTransactionSql = "SELECT 1 FROM ledger WHERE id=$id AND kind<>'OpeningBalance'";
    private const string TransactionGoneMessage = "Transaction no longer exists.";

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

        return SaveTransactionCore(id, draft, ParseCategoryPath(categoryPath));
    }

    private string SaveTransactionCore(string? id, TransactionDraft draft, string[]? categoryParts)
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

            ValidateAccount(c, tx, draft.AccountId, draft.Date, id);
            if (draft.DestinationId is not null)
            {
                ValidateAccount(c, tx, draft.DestinationId, draft.Date, id);
            }

            var categoryId = categoryParts is null ? draft.CategoryId : ResolveCategoryPath(c, tx, categoryParts);
            if (categoryId is not null)
            {
                ValidateCategory(c, tx, id, categoryId, categoryParts is null
                    ? "Choose an active category."
                    : "Restore the archived category before using it for another transaction.");
            }

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

            if (id is not null)
            {
                LedgerSql.Execute(c, tx, "UPDATE import_sources SET locally_modified=1 WHERE transaction_id=$id", ("$id", id));
            }
        });
        return key;
    }

    public void DeleteTransaction(string id) => Write((c, tx) =>
    {
        LedgerSql.Require(c, tx, EditableTransactionSql, id, TransactionGoneMessage);
        LedgerSql.Execute(c, tx, "UPDATE import_sources SET locally_modified=1,transaction_id=NULL WHERE transaction_id=$id", ("$id", id));
        LedgerSql.Execute(c, tx, "DELETE FROM ledger WHERE id=$id", ("$id", id));
    });

    private static void ValidateAccount(SqliteConnection c, SqliteTransaction tx, string id, DateOnly date, string? transactionId)
    {
        DateOnly openingDate;
        bool archived;
        using (var cmd = LedgerSql.Command(c, tx, "SELECT opening_date,archived FROM accounts WHERE id=$id", ("$id", id)))
        using (var reader = cmd.ExecuteReader())
        {
            if (!reader.Read())
            {
                throw new ArgumentException("Account no longer exists.");
            }

            openingDate = LedgerSql.ParseDate(reader.GetString(0));
            archived = reader.GetInt64(1) != 0;
        }

        if (date < openingDate)
        {
            throw new ArgumentException("Transaction date precedes the account's opening date.");
        }

        if (archived && (transactionId is null ||
            LedgerSql.Scalar(c, tx, "SELECT 1 FROM movements WHERE transaction_id=$transaction AND account_id=$account", ("$transaction", transactionId), ("$account", id)) is null))
        {
            throw new ArgumentException("Choose an active account for new movements.");
        }
    }

    /// <summary>Requires the category to exist and, when archived, to be the one the edited transaction already uses.</summary>
    private static void ValidateCategory(SqliteConnection c, SqliteTransaction tx, string? transactionId, string categoryId, string archivedError)
    {
        var archived = LedgerSql.Scalar(c, tx, "SELECT archived FROM categories WHERE id=$id", ("$id", categoryId))
            ?? throw new ArgumentException("Category no longer exists.");
        if (Convert.ToInt64(archived) == 0)
        {
            return;
        }

        var previousCategory = transactionId is null ? null :
            LedgerSql.Scalar(c, tx, "SELECT category_id FROM ledger WHERE id=$id", ("$id", transactionId)) as string;
        if (categoryId != previousCategory)
        {
            throw new ArgumentException(archivedError);
        }
    }

    private static void AddMovement(SqliteConnection c, SqliteTransaction tx, string id, string account, long amount) =>
        LedgerSql.Execute(c, tx, "INSERT INTO movements VALUES($id,$account,$amount)", ("$id", id), ("$account", account), ("$amount", amount));

    private static string? ResolveCategoryPath(SqliteConnection c, SqliteTransaction tx, string[] parts)
    {
        return parts.Aggregate<string?, string?>(null, (current, part) =>
            LedgerSql.Scalar(c, tx,
                "SELECT id FROM categories WHERE parent_id IS $parent AND name=$name COLLATE NOCASE",
                ("$parent", current),
                ("$name", part)) as string ?? throw new ArgumentException("Choose an existing category path."));
    }
}
