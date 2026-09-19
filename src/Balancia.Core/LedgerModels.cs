namespace Balancia.Core;

public enum TransactionKind { Expense, Income, Transfer }

public sealed record TransactionDraft(TransactionKind Kind, DateOnly Date, string Description,
    Money Amount, string AccountId, string? DestinationId = null, string? CategoryId = null, string Memo = "")
{
    public void Validate(DateOnly today)
    {
        if (!Enum.IsDefined(Kind)) throw new ArgumentException("Choose a valid transaction type.");
        if (Amount.Centimes <= 0) throw new ArgumentException("Enter an amount greater than zero.");
        if (Date > today) throw new ArgumentException("Transactions cannot be dated in the future.");
        ArgumentException.ThrowIfNullOrWhiteSpace(AccountId);
        if (Kind == TransactionKind.Transfer)
        {
            if (string.IsNullOrWhiteSpace(DestinationId) || AccountId == DestinationId)
                throw new ArgumentException("Choose two different accounts for a transfer.");
            if (CategoryId is not null) throw new ArgumentException("Transfers do not have expense categories.");
        }
        else if (DestinationId is not null) throw new ArgumentException("Only transfers have a destination account.");
    }
}

public sealed record Account(string Id, string Name, DateOnly OpeningDate, Money OpeningAmount, bool Archived, Money Balance)
{
    public override string ToString() => Name + (Archived ? " (archived)" : "");
}

public sealed record Category(string Id, string Name, string? ParentId, string Path, bool Archived)
{
    public override string ToString() => Path + (Archived ? " (archived)" : "");
}

public sealed record LedgerEntry(string Id, TransactionDraft Draft, string AccountName, string? DestinationName, string? CategoryPath);
public sealed record CategoryTotal(string Name, Money Amount);
public sealed record LedgerSnapshot(IReadOnlyList<Account> Accounts, IReadOnlyList<Category> Categories,
    IReadOnlyList<LedgerEntry> Entries, Money NetWorth, Money MonthlyIncome, Money MonthlyExpenses,
    IReadOnlyList<CategoryTotal> LargestCategories, long Revision);
