using Xunit;

namespace Balancia.Core.Tests;

public class TransactionDraftTests
{
    private static readonly DateOnly Today = new(2026, 1, 15);

    private static readonly Money Ten = Money.FromFrancs(10);

    [Fact]
    public void Validate_ValidExpense_DoesNotThrow()
    {
        // Arrange
        var draft = new TransactionDraft(TransactionKind.Expense, Today, "Groceries", Ten, "account-1");

        // Act
        var exception = Record.Exception(() => draft.Validate(Today));

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public void Validate_ValidTransfer_DoesNotThrow()
    {
        // Arrange
        var draft = new TransactionDraft(TransactionKind.Transfer, Today, "Move funds", Ten, "account-1", "account-2");

        // Act
        var exception = Record.Exception(() => draft.Validate(Today));

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public void Validate_UndefinedKind_ThrowsArgumentException()
    {
        // Arrange
        var draft = new TransactionDraft((TransactionKind)99, Today, "Bad kind", Ten, "account-1");

        // Act
        var validate = () => draft.Validate(Today);

        // Assert
        Assert.Throws<ArgumentException>(validate);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Validate_NonPositiveAmount_ThrowsArgumentException(decimal francs)
    {
        // Arrange
        var draft = new TransactionDraft(TransactionKind.Expense, Today, "Bad amount", Money.FromFrancs(francs), "account-1");

        // Act
        var validate = () => draft.Validate(Today);

        // Assert
        Assert.Throws<ArgumentException>(validate);
    }

    [Fact]
    public void Validate_FutureDate_ThrowsArgumentException()
    {
        // Arrange
        var draft = new TransactionDraft(TransactionKind.Expense, Today.AddDays(1), "Future", Ten, "account-1");

        // Act
        var validate = () => draft.Validate(Today);

        // Assert
        Assert.Throws<ArgumentException>(validate);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_MissingAccountId_ThrowsArgumentException(string accountId)
    {
        // Arrange
        var draft = new TransactionDraft(TransactionKind.Expense, Today, "No account", Ten, accountId);

        // Act
        var validate = () => draft.Validate(Today);

        // Assert
        Assert.ThrowsAny<ArgumentException>(validate);
    }

    [Fact]
    public void Validate_TransferWithoutDestination_ThrowsArgumentException()
    {
        // Arrange
        var draft = new TransactionDraft(TransactionKind.Transfer, Today, "Move funds", Ten, "account-1");

        // Act
        var validate = () => draft.Validate(Today);

        // Assert
        Assert.Throws<ArgumentException>(validate);
    }

    [Fact]
    public void Validate_TransferToSameAccount_ThrowsArgumentException()
    {
        // Arrange
        var draft = new TransactionDraft(TransactionKind.Transfer, Today, "Move funds", Ten, "account-1", "account-1");

        // Act
        var validate = () => draft.Validate(Today);

        // Assert
        Assert.Throws<ArgumentException>(validate);
    }

    [Fact]
    public void Validate_TransferWithCategory_ThrowsArgumentException()
    {
        // Arrange
        var draft = new TransactionDraft(TransactionKind.Transfer, Today, "Move funds", Ten, "account-1", "account-2", CategoryId: "category-1");

        // Act
        var validate = () => draft.Validate(Today);

        // Assert
        Assert.Throws<ArgumentException>(validate);
    }

    [Fact]
    public void Validate_NonTransferWithDestination_ThrowsArgumentException()
    {
        // Arrange
        var draft = new TransactionDraft(TransactionKind.Expense, Today, "Bad destination", Ten, "account-1", "account-2");

        // Act
        var validate = () => draft.Validate(Today);

        // Assert
        Assert.Throws<ArgumentException>(validate);
    }
}
