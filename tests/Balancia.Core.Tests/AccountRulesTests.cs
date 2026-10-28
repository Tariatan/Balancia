using Xunit;

namespace Balancia.Core.Tests;

public class AccountRulesTests
{
    private static readonly DateOnly Today = new(2026, 10, 13);

    private static readonly Account Everyday = new("everyday", "Everyday", new DateOnly(2026, 1, 1), Money.Zero, false, Money.Zero, new DateOnly(2026, 3, 5));
    private static readonly Account Savings = new("savings", "Savings", new DateOnly(2026, 2, 1), Money.Zero, false, Money.Zero);

    private static readonly Account[] All = [Everyday, Savings];

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CheckSave_BlankName_AsksForAName(string name)
    {
        // Act
        var message = AccountRules.CheckSave(All, null, name, Today, Today);

        // Assert
        Assert.Equal(AccountRules.NameRequiredMessage, message);
    }

    [Fact]
    public void CheckSave_NewAccount_IsAccepted()
    {
        // Act
        var message = AccountRules.CheckSave(All, null, "Cash", Today, Today);

        // Assert
        Assert.Null(message);
    }

    [Fact]
    public void CheckSave_OpeningDateAfterToday_IsRejected()
    {
        // Act
        var message = AccountRules.CheckSave(All, null, "Cash", Today.AddDays(1), Today);

        // Assert
        Assert.Equal(AccountRules.OpeningDateInFutureMessage, message);
    }

    [Fact]
    public void CheckSave_OpeningDateAfterTheFirstTransaction_IsRejected()
    {
        // Act
        var message = AccountRules.CheckSave(All, "everyday", "Everyday", new DateOnly(2026, 3, 6), Today);

        // Assert
        Assert.Equal(AccountRules.OpeningAfterFirstTransactionMessage, message);
    }

    [Fact]
    public void CheckSave_OpeningDateOnTheFirstTransaction_IsAccepted()
    {
        // Act
        var message = AccountRules.CheckSave(All, "everyday", "Everyday", new DateOnly(2026, 3, 5), Today);

        // Assert
        Assert.Null(message);
    }

    [Fact]
    public void CheckSave_AccountWithoutTransactions_MayOpenOnAnyPastDate()
    {
        // Act
        var message = AccountRules.CheckSave(All, "savings", "Savings", Today, Today);

        // Assert
        Assert.Null(message);
    }

    [Theory]
    [InlineData("Savings")]
    [InlineData("savings")]
    [InlineData("  SAVINGS ")]
    public void CheckSave_NameOfAnotherAccount_IsRejected(string name)
    {
        // Act
        var message = AccountRules.CheckSave(All, "everyday", name, new DateOnly(2026, 1, 1), Today);

        // Assert
        Assert.Equal(AccountRules.DuplicateNameMessage, message);
    }

    [Fact]
    public void CheckSave_KeepingItsOwnName_IsAccepted()
    {
        // Act
        var message = AccountRules.CheckSave(All, "savings", "SAVINGS", new DateOnly(2026, 2, 1), Today);

        // Assert
        Assert.Null(message);
    }

    [Fact]
    public void CheckDelete_AccountWithTransactions_IsRejected()
    {
        // Act
        var message = AccountRules.CheckDelete(All, "everyday");

        // Assert
        Assert.Equal(AccountRules.DeleteWithTransactionsMessage, message);
    }

    [Fact]
    public void CheckDelete_AccountWithoutTransactions_IsAccepted()
    {
        // Act
        var message = AccountRules.CheckDelete(All, "savings");

        // Assert
        Assert.Null(message);
    }

    [Theory]
    [InlineData("2026-01-31", true)]
    [InlineData("2026-02-01", false)]
    [InlineData("2026-02-02", false)]
    public void CheckTransactionDate_BeforeTheOpeningDate_IsRejected(string date, bool rejected)
    {
        // Act
        var message = AccountRules.CheckTransactionDate(new DateOnly(2026, 2, 1), DateOnly.Parse(date));

        // Assert
        Assert.Equal(rejected ? AccountRules.TransactionBeforeOpeningMessage : null, message);
    }
}
