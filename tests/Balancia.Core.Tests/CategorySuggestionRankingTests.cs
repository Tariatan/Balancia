using Xunit;

namespace Balancia.Core.Tests;

public class CategorySuggestionRankingTests
{
    [Fact]
    public void Build_ExactWordPrefixAndSegmentMatches_RanksExactAboveWordPrefixAboveSegment()
    {
        // Arrange
        string[] categories = ["Food/Lunch", "Lunch", "Lunch Break", "Groceries"];

        // Act
        var suggestions = CategorySuggestionRanking.Build(categories, [], "Lunch");

        // Assert
        Assert.Equal(
            [
                ("Lunch", CategorySuggestionSection.TopMatch),
                ("Food/Lunch", CategorySuggestionSection.Other),
                ("Lunch Break", null),
            ],
            Project(suggestions));
    }

    [Fact]
    public void Build_LowercaseQuery_MatchesCaseInsensitively()
    {
        // Act
        var suggestions = CategorySuggestionRanking.Build(["Groceries"], [], "grocer");

        // Assert
        Assert.Equal([("Groceries", CategorySuggestionSection.TopMatch)], Project(suggestions));
    }

    [Fact]
    public void Build_UsedCategories_PlacesRegularMatchesBeforeTopMatchWithoutDuplicates()
    {
        // Act
        var suggestions = CategorySuggestionRanking.Build(["Alpha", "Beta", "Gamma"], ["Gamma", "Beta"], "a");

        // Assert
        Assert.Equal(
            [
                ("Gamma", CategorySuggestionSection.Regular),
                ("Beta", null),
                ("Alpha", CategorySuggestionSection.TopMatch),
            ],
            Project(suggestions));
    }

    [Fact]
    public void Build_EmptyQuery_ReturnsAllPathsWithoutTopMatch()
    {
        // Act
        var suggestions = CategorySuggestionRanking.Build(["Beta", "Alpha"], [], "");

        // Assert
        Assert.Equal(
            [
                ("Alpha", CategorySuggestionSection.Other),
                ("Beta", null),
            ],
            Project(suggestions));
    }

    [Fact]
    public void Build_DuplicatePathsDifferingByCase_CollapsesToOne()
    {
        // Act
        var suggestions = CategorySuggestionRanking.Build(["Food", "food"], [], "foo");

        // Assert
        Assert.Single(suggestions);
    }

    [Fact]
    public void Build_NoMatch_ReturnsEmpty()
    {
        // Act
        var suggestions = CategorySuggestionRanking.Build(["Groceries"], [], "zzz");

        // Assert
        Assert.Empty(suggestions);
    }

    [Theory]
    [InlineData("shop", "Shop / Misc")]
    [InlineData("misc", "Shop / Misc")]
    [InlineData("service", "Services / Health insurance")]
    [InlineData("health", "Services / Health insurance")]
    [InlineData("MISC", "Shop / Misc")]
    public void Build_UsedParentOrChildMatch_AppearsInRegular(string query, string path)
    {
        // Arrange
        string[] categories = ["Shop / Misc", "Services / Health insurance"];

        // Act
        var suggestions = CategorySuggestionRanking.Build(categories, categories, query);

        // Assert
        Assert.Equal([(path, CategorySuggestionSection.Regular)], Project(suggestions));
    }

    [Fact]
    public void Build_MoreThanTwoUsedMatches_FiltersBeforeLimitAndKeepsRemainingChoices()
    {
        // Arrange
        string[] categories = ["Popular", "Shop / Misc", "Misc / Misc", "Other / Misc", "Unused / Misc"];

        // Act
        var suggestions = CategorySuggestionRanking.Build(categories, categories.Take(4), "misc");

        // Assert
        Assert.Equal(
            [
                ("Shop / Misc", CategorySuggestionSection.Regular),
                ("Misc / Misc", null),
                ("Other / Misc", CategorySuggestionSection.TopMatch),
                ("Unused / Misc", CategorySuggestionSection.Other),
            ],
            Project(suggestions));
    }

    [Fact]
    public void Highlight_SelectedIndex_MarksOnlyThatSuggestionAsKeyboardSelected()
    {
        // Arrange
        var suggestions = CategorySuggestionRanking.Build(["Alpha", "Beta"], [], "");

        // Act
        var highlighted = CategorySuggestionRanking.Highlight(suggestions, 1);

        // Assert
        Assert.Equal([false, true], highlighted.Select(suggestion => suggestion.IsKeyboardSelected));
    }

    private static IEnumerable<(string Path, CategorySuggestionSection? Section)> Project(
        IEnumerable<CategorySuggestion> suggestions) =>
        suggestions.Select(suggestion => (suggestion.Path, suggestion.Section));
}
