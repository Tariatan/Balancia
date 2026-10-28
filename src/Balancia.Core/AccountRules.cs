namespace Balancia.Core;

/// <summary>
/// The rules for accounts, so that the ledger store, the dialogs and the transaction form enforce one definition.
/// The checks run over a plain list of accounts, which the store reads inside its write transaction and Desktop takes from its snapshot.
/// </summary>
public static class AccountRules
{
    public static readonly string NameRequiredMessage = "Enter an account name.";

    public static readonly string OpeningDateInFutureMessage = "Opening date cannot be in the future.";

    public static readonly string OpeningAfterFirstTransactionMessage = "Opening date must be on or before the first transaction in this account.";

    public static readonly string DuplicateNameMessage = "An account with this name already exists.";

    public static readonly string DeleteWithTransactionsMessage = "An account with transactions cannot be deleted. Archive it instead.";

    public static readonly string TransactionBeforeOpeningMessage = "Transaction date precedes the account's opening date.";

    /// <summary>
    /// Why saving the account cannot go ahead, or null when it can. <paramref name="id"/> is null for a new account;
    /// <paramref name="accounts"/> holds every existing account, including the one being edited.
    /// </summary>
    public static string? CheckSave(IReadOnlyList<Account> accounts, string? id, string name, DateOnly openingDate, DateOnly today)
    {
        var trimmedName = name.Trim();
        if (trimmedName.Length == 0)
        {
            return NameRequiredMessage;
        }

        if (openingDate > today)
        {
            return OpeningDateInFutureMessage;
        }

        if (accounts.FirstOrDefault(a => a.Id == id)?.FirstTransactionDate is { } first && openingDate > first)
        {
            return OpeningAfterFirstTransactionMessage;
        }

        return accounts.Any(a => a.Id != id && string.Equals(a.Name, trimmedName, StringComparison.OrdinalIgnoreCase))
            ? DuplicateNameMessage
            : null;
    }

    /// <summary>Why the account cannot be deleted (archive it instead), or null when it can.</summary>
    public static string? CheckDelete(IReadOnlyList<Account> accounts, string id) =>
        accounts.Any(a => a.Id == id && a.FirstTransactionDate is not null) ? DeleteWithTransactionsMessage : null;

    /// <summary>Why a transaction dated <paramref name="date"/> cannot go on an account opened on <paramref name="openingDate"/>, or null when it can.</summary>
    public static string? CheckTransactionDate(DateOnly openingDate, DateOnly date) =>
        date < openingDate ? TransactionBeforeOpeningMessage : null;
}
