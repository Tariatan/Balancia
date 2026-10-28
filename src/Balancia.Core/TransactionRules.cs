namespace Balancia.Core;

/// <summary>
/// What a transaction that is being edited already uses: its accounts and its category. An archived account or category stays usable
/// for that transaction, and for no other.
/// </summary>
public sealed record SavedTransaction(IReadOnlyList<string> AccountIds, string? CategoryId)
{
    public static SavedTransaction From(TransactionDraft draft) =>
        new([.. new[] { draft.AccountId, draft.DestinationId }.OfType<string>()], draft.CategoryId);
}

/// <summary>
/// The rules for saving a transaction against its accounts and categories, so that the ledger store and the transaction form enforce one
/// definition. The checks run over plain lists, which the store reads inside its write transaction and Desktop takes from its snapshot.
/// The shape of the draft itself (amount, date, transfer accounts) stays in <see cref="TransactionDraft.Validate"/>.
/// </summary>
public static class TransactionRules
{
    public static readonly string AccountMissingMessage = "Account no longer exists.";

    public static readonly string AccountArchivedMessage = "Choose an active account for new movements.";

    public static readonly string CategoryMissingMessage = "Category no longer exists.";

    public static readonly string CategoryArchivedMessage = "Choose an active category.";

    /// <summary>The message for a path that names no category; it shows the path in the spelling the ledger uses.</summary>
    public static string PathUnknownMessage(string? path) => $"Choose an existing category path. No category \"{CategoryRules.Prettify(path)}\".";

    public static readonly string PathArchivedMessage = "Restore the archived category before using it for another transaction.";

    /// <summary>
    /// Why the transaction cannot be saved, or null when it can: the accounts and the date come first, then the category path.
    /// <paramref name="accounts"/> needs at least the account and the destination, <paramref name="categories"/> every category.
    /// <paramref name="existing"/> is null for a new transaction. An empty <paramref name="categoryPath"/> means no category.
    /// </summary>
    public static string? CheckSave(IReadOnlyList<Account> accounts, IReadOnlyList<Category> categories, DateOnly date,
        string accountId, string? destinationId, string? categoryPath, SavedTransaction? existing) =>
        CheckAccounts(accounts, date, accountId, destinationId, existing) ?? CheckCategoryPath(categories, categoryPath, existing?.CategoryId);

    /// <summary>
    /// Why the account (and the destination of a transfer) cannot take a movement dated <paramref name="date"/>, or null when it can:
    /// each must exist, be open by that date, and be active unless the edited transaction already uses it.
    /// </summary>
    public static string? CheckAccounts(IReadOnlyList<Account> accounts, DateOnly date, string accountId, string? destinationId, SavedTransaction? existing) =>
        CheckAccount(accounts, accountId, date, existing) ?? (destinationId is null ? null : CheckAccount(accounts, destinationId, date, existing));

    private static string? CheckAccount(IReadOnlyList<Account> accounts, string id, DateOnly date, SavedTransaction? existing)
    {
        if (accounts.FirstOrDefault(a => a.Id == id) is not { } account)
        {
            return AccountMissingMessage;
        }

        if (AccountRules.CheckTransactionDate(account.OpeningDate, date) is { } message)
        {
            return message;
        }

        return account.Archived && existing?.AccountIds.Contains(id) != true ? AccountArchivedMessage : null;
    }

    /// <summary>Why the category given by ID cannot be used, or null when it can: it must exist and be active unless the edited transaction uses it.</summary>
    public static string? CheckCategoryId(IReadOnlyList<Category> categories, string id, SavedTransaction? existing)
    {
        if (categories.FirstOrDefault(c => c.Id == id) is not { } category)
        {
            return CategoryMissingMessage;
        }

        return category.Archived && id != existing?.CategoryId ? CategoryArchivedMessage : null;
    }

    /// <summary>
    /// Why the typed category path cannot be used, or null when it can: it must be well formed and name an existing category, in any letter
    /// case and with any spacing around '/' (see <see cref="CategoryRules.Prettify"/>), and that category must be active unless it is
    /// <paramref name="existingCategoryId"/>. An empty path means no category.
    /// </summary>
    public static string? CheckCategoryPath(IReadOnlyList<Category> categories, string? path, string? existingCategoryId)
    {
        if (!CategoryRules.TryParsePath(path, out var names))
        {
            return CategoryRules.InvalidPathMessage;
        }

        if (names.Length == 0)
        {
            return null;
        }

        if (FindByPath(categories, path) is not { } category)
        {
            return PathUnknownMessage(path);
        }

        return category.Archived && category.Id != existingCategoryId ? PathArchivedMessage : null;
    }

    /// <summary>
    /// The category a typed path names (letter case and the spacing around '/' ignored), or null when the path is empty, malformed or names none.
    /// A name never contains '/', so the names of a path point to at most one category.
    /// </summary>
    public static Category? FindByPath(IReadOnlyList<Category> categories, string? path)
    {
        if (!CategoryRules.TryParsePath(path, out var names) || names.Length == 0)
        {
            return null;
        }

        string? parentId = null;
        Category? found = null;
        foreach (var name in names)
        {
            found = CategoryRules.FindChild(categories, parentId, name);
            if (found is null)
            {
                return null;
            }

            parentId = found.Id;
        }

        return found;
    }
}
