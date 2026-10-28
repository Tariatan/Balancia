using Xunit;

namespace Balancia.Core.Tests;

public class ReminderRulesTests
{
    private static readonly DateOnly Date = new(2026, 10, 13);
    private static readonly Money Amount = Money.FromFrancs(10m);

    private static readonly ReminderTemplate Rent = new("rent", "Rent", Date, Amount, 1, false);
    private static readonly ReminderTemplate Phone = new("phone", "Phone", Date, Amount, 1, false);

    private static readonly ReminderTemplate[] Active = [Rent, Phone];

    [Fact]
    public void CheckSave_ValidNewReminder_IsAccepted()
    {
        // Act
        var message = ReminderRules.CheckSave(Active, null, "Insurance", Amount, 12, false);

        // Assert
        Assert.Null(message);
    }

    [Fact]
    public void CheckSave_BlankDescription_AsksForOne()
    {
        // Act
        var message = ReminderRules.CheckSave(Active, null, "   ", Amount, 1, false);

        // Assert
        Assert.Equal(ReminderRules.DescriptionRequiredMessage, message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public void CheckSave_AmountNotAboveZero_IsRejected(long centimes)
    {
        // Act
        var message = ReminderRules.CheckSave(Active, null, "Insurance", new Money(centimes), 1, false);

        // Assert
        Assert.Equal(ReminderRules.AmountMessage, message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void CheckSave_IntervalNotAboveZero_IsRejected(int months)
    {
        // Act
        var message = ReminderRules.CheckSave(Active, null, "Insurance", Amount, months, false);

        // Assert
        Assert.Equal(ReminderRules.IntervalMessage, message);
    }

    [Fact]
    public void CheckSave_DescriptionOfAnotherActiveReminder_IsRejected()
    {
        // Act
        var message = ReminderRules.CheckSave(Active, null, "  Rent ", Amount, 1, false);

        // Assert
        Assert.Equal(ReminderRules.DuplicateDescriptionMessage, message);
    }

    [Fact]
    public void CheckSave_DescriptionDifferingOnlyInLetterCase_IsAccepted()
    {
        // Act
        var message = ReminderRules.CheckSave(Active, null, "rent", Amount, 1, false);

        // Assert
        Assert.Null(message);
    }

    [Fact]
    public void CheckSave_KeepingItsOwnDescription_IsAccepted()
    {
        // Act
        var message = ReminderRules.CheckSave(Active, "rent", "Rent", Amount, 3, false);

        // Assert
        Assert.Null(message);
    }

    [Fact]
    public void CheckSave_ArchivedReminderWithAnActiveDescription_IsAccepted()
    {
        // Act
        var message = ReminderRules.CheckSave(Active, null, "Rent", Amount, 1, true);

        // Assert
        Assert.Null(message);
    }

    [Fact]
    public void IsDuplicateDescription_ArchivedTemplatesAreIgnored()
    {
        // Arrange
        ReminderTemplate[] templates = [Rent with { Archived = true }];

        // Act
        var duplicate = ReminderRules.IsDuplicateDescription(templates, null, "Rent");

        // Assert
        Assert.False(duplicate);
    }
}
