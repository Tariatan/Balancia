using Balancia.Core;

namespace Balancia.Desktop;

/// <summary>
/// The rules of the add and edit transaction form that need no controls: what the form offers, what it starts with,
/// how the typed category path is read and checked, and how the entered values become a draft.
/// </summary>
internal static class TransactionFormRules
{
    /// <summary>The accounts the form offers: the active ones, plus an archived account that the entry being edited already uses.</summary>
    internal static IReadOnlyList<Account> AvailableAccounts(IReadOnlyList<Account> accounts, TransactionDraft? existing) =>
        [.. accounts.Where(a => !a.Archived || a.Id == existing?.AccountId || a.Id == existing?.DestinationId)];

    /// <summary>
    /// The account the form starts on: the one being edited, otherwise the configured default, otherwise the one used last.
    /// An id that is not among <paramref name="accounts"/> falls back to the first account, without trying the later ids.
    /// </summary>
    internal static Account DefaultAccount(IReadOnlyList<Account> accounts, string? existingAccountId, string? defaultAccountId, string? lastAccountId)
    {
        var wanted = existingAccountId ?? defaultAccountId ?? lastAccountId;
        return accounts.FirstOrDefault(a => a.Id == wanted) ?? accounts[0];
    }

    /// <summary>The categories the form offers: the active ones, plus the archived category that the entry being edited already uses.</summary>
    internal static IReadOnlyList<Category> AvailableCategories(IReadOnlyList<Category> categories, string? currentCategoryId) =>
        [.. categories.Where(c => !c.Archived || c.Id == currentCategoryId)];

    /// <summary>The typed category path, trimmed and given the stored spelling when it matches an available category; null when empty.</summary>
    internal static string? ReadCategoryPath(string? text, IReadOnlyList<Category> available)
    {
        var path = text?.Trim();
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        return available.FirstOrDefault(candidate =>
            string.Equals(candidate.Path, path, StringComparison.OrdinalIgnoreCase))?.Path ?? path;
    }

    /// <summary>The category to save: none for a transfer, otherwise the typed path.</summary>
    internal static string? CategoryPathFor(TransactionKind kind, string? text, IReadOnlyList<Category> available) =>
        kind == TransactionKind.Transfer ? null : ReadCategoryPath(text, available);

    /// <summary>Why <paramref name="path"/> cannot be used, or null when it can. <paramref name="all"/> tells an archived category from an unknown one.</summary>
    internal static string? CategoryPathError(string? path, IReadOnlyList<Category> available, IReadOnlyList<Category> all)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        if (!CategoryRules.TryParsePath(path, out _))
        {
            return CategoryRules.InvalidPathMessage;
        }

        if (available.Any(c => string.Equals(c.Path, path, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        return all.Any(c => string.Equals(c.Path, path, StringComparison.OrdinalIgnoreCase))
            ? "Restore the archived category before using it for another transaction."
            : "Choose an existing category path.";
    }

    /// <summary>
    /// Why the form cannot be saved, or null when it can: the date may not precede the opening date of the account (or of the destination of a transfer),
    /// and a transaction that is not a transfer needs a usable category path.
    /// </summary>
    internal static string? Validate(TransactionKind kind, DateOnly date, Account account, Account? destination, string? categoryText,
        IReadOnlyList<Category> available, IReadOnlyList<Category> all)
    {
        var selectedDestination = kind == TransactionKind.Transfer ? destination : null;
        if (date < account.OpeningDate || date < selectedDestination?.OpeningDate)
        {
            return "Transaction date precedes the account's opening date.";
        }

        return kind == TransactionKind.Transfer ? null : CategoryPathError(ReadCategoryPath(categoryText, available), available, all);
    }

    /// <summary>The draft for the entered values; only a transfer keeps its destination, and the category is saved separately by path.</summary>
    internal static TransactionDraft ToDraft(TransactionKind kind, DateOnly date, string description, Money amount, string accountId,
        string? destinationId, string memo) =>
        new(kind, date, description, amount, accountId, kind == TransactionKind.Transfer ? destinationId : null, Memo: memo);
}
