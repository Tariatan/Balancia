using Xunit;

namespace Balancia.Core.Tests;

public class CategoryRulesTests
{
    private static readonly Category Food = new("food", "Food", null, "Food", false);
    private static readonly Category Lunch = new("lunch", "Lunch", "food", "Food / Lunch", false);
    private static readonly Category Travel = new("travel", "Travel", null, "Travel", false);
    private static readonly Category OldTop = new("old", "Old", null, "Old", true);
    private static readonly Category OldChild = new("oldchild", "Kept", "old", "Old / Kept", true);

    private static readonly Category[] All = [Food, Lunch, Travel, OldTop, OldChild];

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryParsePath_EmptyText_ReturnsNoNames(string? path)
    {
        // Act
        var parsed = CategoryRules.TryParsePath(path, out var names);

        // Assert
        Assert.True(parsed);
        Assert.Empty(names!);
    }

    [Theory]
    [InlineData("Food", new[] { "Food" })]
    [InlineData("  Food  ", new[] { "Food" })]
    [InlineData("Food/Lunch", new[] { "Food", "Lunch" })]
    [InlineData(" Food /  Lunch ", new[] { "Food", "Lunch" })]
    public void TryParsePath_OneOrTwoNames_ReturnsTheTrimmedNames(string path, string[] expected)
    {
        // Act
        var parsed = CategoryRules.TryParsePath(path, out var names);

        // Assert
        Assert.True(parsed);
        Assert.Equal(expected, names);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("Food", "Food")]
    [InlineData("  Food  ", "Food")]
    [InlineData("Food / Lunch", "Food / Lunch")]
    [InlineData("Food/Lunch", "Food / Lunch")]
    [InlineData("Food /Lunch", "Food / Lunch")]
    [InlineData("Food/ Lunch", "Food / Lunch")]
    [InlineData("  Food  /  Lunch  ", "Food / Lunch")]
    [InlineData("food/lunch", "food / lunch")] // letter case is kept
    [InlineData("Food/", "Food /")] // a half typed path stays recognisable
    [InlineData("A/B/C", "A / B / C")] // nothing is validated
    public void Prettify_AnySpacingAroundTheSeparator_GivesTheLedgerSpelling(string? path, string expected) =>
        Assert.Equal(expected, CategoryRules.Prettify(path));

    [Theory]
    [InlineData(null, "Food", "food")]
    [InlineData(null, "FOOD", "food")]
    [InlineData("food", "lunch", "lunch")]
    [InlineData("food", "LUNCH", "lunch")]
    [InlineData(null, "Épicerie", "epic")]
    [InlineData(null, "épicerie", "epic")] // letter case is folded for every character, not only ASCII
    public void FindChild_AnyLetterCase_FindsTheCategoryUnderThatParent(string? parentId, string name, string expectedId)
    {
        // Arrange
        Category[] categories =
        [
            new("food", "Food", null, "Food", false),
            new("lunch", "Lunch", "food", "Food / Lunch", false),
            new("epic", "Épicerie", null, "Épicerie", false),
        ];

        // Act and Assert
        Assert.Equal(expectedId, CategoryRules.FindChild(categories, parentId, name)?.Id);
    }

    [Theory]
    [InlineData(null, "Lunch")] // a subcategory is not a top-level category
    [InlineData("food", "Food")]
    [InlineData("other", "Lunch")]
    [InlineData(null, "Dinner")]
    public void FindChild_NoSuchNameUnderThatParent_FindsNothing(string? parentId, string name)
    {
        // Arrange
        Category[] categories =
        [
            new("food", "Food", null, "Food", false),
            new("lunch", "Lunch", "food", "Food / Lunch", false),
        ];

        // Act and Assert
        Assert.Null(CategoryRules.FindChild(categories, parentId, name));
    }

    [Theory]
    [InlineData("A/B/C")]
    [InlineData("Food/")]
    [InlineData("/Lunch")]
    [InlineData("Food/ /Lunch")]
    [InlineData("/")]
    public void TryParsePath_MoreThanTwoLevelsOrAnEmptySide_IsRejected(string path)
    {
        // Act
        var parsed = CategoryRules.TryParsePath(path, out var names);

        // Assert
        Assert.False(parsed);
        Assert.Null(names);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("A/B")]
    public void CheckSave_BlankNameOrNameWithSlash_AsksForAPlainName(string name)
    {
        // Act
        var message = CategoryRules.CheckSave(All, null, name, null, false);

        // Assert
        Assert.Equal(CategoryRules.InvalidNameMessage, message);
    }

    [Fact]
    public void CheckSave_NewTopLevelCategory_IsAccepted()
    {
        // Act
        var message = CategoryRules.CheckSave(All, null, "Health", null, false);

        // Assert
        Assert.Null(message);
    }

    [Fact]
    public void CheckSave_OwnParent_IsRejected()
    {
        // Act
        var message = CategoryRules.CheckSave(All, "food", "Food", "food", false);

        // Assert
        Assert.Equal(CategoryRules.OwnParentMessage, message);
    }

    [Theory]
    [InlineData("lunch")]
    [InlineData("missing")]
    public void CheckSave_ParentThatIsNotATopLevelCategory_IsRejected(string parentId)
    {
        // Act
        var message = CategoryRules.CheckSave(All, null, "Snack", parentId, false);

        // Assert
        Assert.Equal(CategoryRules.ParentNotTopLevelMessage, message);
    }

    [Fact]
    public void CheckSave_NewChildOfAnArchivedParent_AsksToRestoreTheParent()
    {
        // Act
        var message = CategoryRules.CheckSave(All, null, "Fresh", "old", false);

        // Assert
        Assert.Equal(CategoryRules.ParentArchivedMessage, message);
    }

    [Fact]
    public void CheckSave_ArchivedChildStayingUnderItsArchivedParent_IsAccepted()
    {
        // Act
        var message = CategoryRules.CheckSave(All, "oldchild", "Kept", "old", true);

        // Assert
        Assert.Null(message);
    }

    [Fact]
    public void CheckSave_RestoringAChildUnderAnArchivedParent_AsksToRestoreTheParent()
    {
        // Act
        var message = CategoryRules.CheckSave(All, "oldchild", "Kept", "old", false);

        // Assert
        Assert.Equal(CategoryRules.ParentArchivedMessage, message);
    }

    [Fact]
    public void CheckSave_MovingACategoryWithSubcategoriesUnderAParent_IsRejected()
    {
        // Act
        var message = CategoryRules.CheckSave(All, "food", "Food", "travel", false);

        // Assert
        Assert.Equal(CategoryRules.MustRemainTopLevelMessage, message);
    }

    [Theory]
    [InlineData("Travel")]
    [InlineData("travel")]
    [InlineData("  TRAVEL ")]
    public void CheckSave_NameUsedByAnotherTopLevelCategory_IsRejected(string name)
    {
        // Act
        var message = CategoryRules.CheckSave(All, null, name, null, false);

        // Assert
        Assert.Equal(CategoryRules.DuplicateNameMessage, message);
    }

    [Fact]
    public void CheckSave_NameUsedByASiblingOnly_IsRejectedUnderThatParentAndAcceptedElsewhere()
    {
        // Act
        var sibling = CategoryRules.CheckSave(All, null, "lunch", "food", false);
        var elsewhere = CategoryRules.CheckSave(All, null, "Lunch", "travel", false);

        // Assert
        Assert.Equal(CategoryRules.DuplicateNameMessage, sibling);
        Assert.Null(elsewhere);
    }

    [Fact]
    public void CheckSave_KeepingItsOwnName_IsAccepted()
    {
        // Act
        var message = CategoryRules.CheckSave(All, "travel", "Travel", null, true);

        // Assert
        Assert.Null(message);
    }

    [Fact]
    public void CheckDelete_CategoryWithSubcategories_IsRejected()
    {
        // Act
        var message = CategoryRules.CheckDelete(All, "food");

        // Assert
        Assert.Equal(CategoryRules.DeleteWithSubcategoriesMessage, message);
    }

    [Fact]
    public void CheckDelete_CategoryUsedByTransactions_IsRejected()
    {
        // Arrange
        Category[] categories = [Travel with { HasTransactions = true }];

        // Act
        var message = CategoryRules.CheckDelete(categories, "travel");

        // Assert
        Assert.Equal(CategoryRules.DeleteUsedMessage, message);
    }

    [Fact]
    public void CheckDelete_UnusedCategoryWithoutChildren_IsAccepted()
    {
        // Act
        var message = CategoryRules.CheckDelete(All, "travel");

        // Assert
        Assert.Null(message);
    }
}
