using Xunit;

namespace Balancia.Core.Tests;

public class TransactionDraftTests
{
    private static readonly DateOnly Today = new(2026, 1, 15);

    [Fact]
    public void Validate_ValidExpense_DoesNotThrow()
    {
        var draft = new TransactionDraft(TransactionKind.Expense, Today, "Groceries",
            Money.FromFrancs(10), "account-1");

        draft.Validate(Today);
    }

    [Fact]
    public void Validate_ValidTransfer_DoesNotThrow()
    {
        var draft = new TransactionDraft(TransactionKind.Transfer, Today, "Move funds",
            Money.FromFrancs(10), "account-1", "account-2");

        draft.Validate(Today);
    }

    [Fact]
    public void Validate_UndefinedKind_ThrowsArgumentException()
    {
        var draft = new TransactionDraft((TransactionKind)99, Today, "Bad kind",
            Money.FromFrancs(10), "account-1");

        Assert.Throws<ArgumentException>(() => draft.Validate(Today));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Validate_NonPositiveAmount_ThrowsArgumentException(decimal francs)
    {
        var draft = new TransactionDraft(TransactionKind.Expense, Today, "Bad amount",
            Money.FromFrancs(francs), "account-1");

        Assert.Throws<ArgumentException>(() => draft.Validate(Today));
    }

    [Fact]
    public void Validate_FutureDate_ThrowsArgumentException()
    {
        var draft = new TransactionDraft(TransactionKind.Expense, Today.AddDays(1), "Future",
            Money.FromFrancs(10), "account-1");

        Assert.Throws<ArgumentException>(() => draft.Validate(Today));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_MissingAccountId_ThrowsArgumentException(string accountId)
    {
        var draft = new TransactionDraft(TransactionKind.Expense, Today, "No account",
            Money.FromFrancs(10), accountId);

        Assert.ThrowsAny<ArgumentException>(() => draft.Validate(Today));
    }

    [Fact]
    public void Validate_TransferWithoutDestination_ThrowsArgumentException()
    {
        var draft = new TransactionDraft(TransactionKind.Transfer, Today, "Move funds",
            Money.FromFrancs(10), "account-1");

        Assert.Throws<ArgumentException>(() => draft.Validate(Today));
    }

    [Fact]
    public void Validate_TransferToSameAccount_ThrowsArgumentException()
    {
        var draft = new TransactionDraft(TransactionKind.Transfer, Today, "Move funds",
            Money.FromFrancs(10), "account-1", "account-1");

        Assert.Throws<ArgumentException>(() => draft.Validate(Today));
    }

    [Fact]
    public void Validate_TransferWithCategory_ThrowsArgumentException()
    {
        var draft = new TransactionDraft(TransactionKind.Transfer, Today, "Move funds",
            Money.FromFrancs(10), "account-1", "account-2", CategoryId: "category-1");

        Assert.Throws<ArgumentException>(() => draft.Validate(Today));
    }

    [Fact]
    public void Validate_NonTransferWithDestination_ThrowsArgumentException()
    {
        var draft = new TransactionDraft(TransactionKind.Expense, Today, "Bad destination",
            Money.FromFrancs(10), "account-1", "account-2");

        Assert.Throws<ArgumentException>(() => draft.Validate(Today));
    }
}
