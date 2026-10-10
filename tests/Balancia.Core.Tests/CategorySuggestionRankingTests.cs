using Xunit;

namespace Balancia.Core.Tests;

public class CategorySuggestionRankingTests
{
    [Fact]
    public void Build_RanksExactMatchAboveWordPrefixAboveSegmentMatch()
    {
        var suggestions = CategorySuggestionRanking.Build(
            ["Food/Lunch", "Lunch", "Lunch Break", "Groceries"], [], "Lunch");

        Assert.Collection(suggestions,
            first =>
            {
                Assert.Equal("Lunch", first.Path);
                Assert.Equal(CategorySuggestionSection.TopMatch, first.Section);
            },
            second =>
            {
                Assert.Equal("Food/Lunch", second.Path);
                Assert.Equal(CategorySuggestionSection.Other, second.Section);
            },
            third =>
            {
                Assert.Equal("Lunch Break", third.Path);
                Assert.Null(third.Section);
            });
    }

    [Fact]
    public void Build_MatchIsCaseInsensitive()
    {
        var suggestions = CategorySuggestionRanking.Build(["Groceries"], [], "grocer");

        var topMatch = Assert.Single(suggestions);
        Assert.Equal("Groceries", topMatch.Path);
        Assert.Equal(CategorySuggestionSection.TopMatch, topMatch.Section);
    }

    [Fact]
    public void Build_RecentMatchesFollowTopMatchAndExcludeIt()
    {
        var suggestions = CategorySuggestionRanking.Build(
            ["Alpha", "Beta", "Gamma"], ["Gamma", "Beta"], "a");

        Assert.Collection(suggestions,
            first =>
            {
                Assert.Equal("Alpha", first.Path);
                Assert.Equal(CategorySuggestionSection.TopMatch, first.Section);
            },
            second =>
            {
                Assert.Equal("Gamma", second.Path);
                Assert.Equal(CategorySuggestionSection.Recent, second.Section);
            },
            third =>
            {
                Assert.Equal("Beta", third.Path);
                Assert.Null(third.Section);
            });
    }

    [Fact]
    public void Build_EmptyQuery_ReturnsAllPathsWithoutTopMatch()
    {
        var suggestions = CategorySuggestionRanking.Build(["Beta", "Alpha"], [], "");

        Assert.Collection(suggestions,
            first =>
            {
                Assert.Equal("Alpha", first.Path);
                Assert.Equal(CategorySuggestionSection.Other, first.Section);
            },
            second =>
            {
                Assert.Equal("Beta", second.Path);
                Assert.Null(second.Section);
            });
    }

    [Fact]
    public void Build_DuplicatePathsDifferingByCase_AreCollapsed()
    {
        var suggestions = CategorySuggestionRanking.Build(["Food", "food"], [], "foo");

        Assert.Single(suggestions);
    }

    [Fact]
    public void Build_NoMatch_ReturnsEmpty() =>
        Assert.Empty(CategorySuggestionRanking.Build(["Groceries"], [], "zzz"));

    [Theory]
    [InlineData("shop", "Shop / Misc")]
    [InlineData("misc", "Shop / Misc")]
    [InlineData("service", "Services / Health insurance")]
    [InlineData("health", "Services / Health insurance")]
    [InlineData("MISC", "Shop / Misc")]
    public void Build_RememberedParentOrChildMatch_AppearsInRecent(string query, string path)
    {
        // Arrange
        string[] categories = ["Shop / Misc", "Services / Health insurance"];

        // Act
        var suggestions = CategorySuggestionRanking.Build(categories, categories, query);

        // Assert
        var suggestion = Assert.Single(suggestions);
        Assert.Equal(path, suggestion.Path);
        Assert.Equal(CategorySuggestionSection.Recent, suggestion.Section);
    }

    [Fact]
    public void Highlight_MarksOnlySelectedIndexAsKeyboardSelected()
    {
        var suggestions = CategorySuggestionRanking.Build(["Alpha", "Beta"], [], "");

        var highlighted = CategorySuggestionRanking.Highlight(suggestions, 1);

        Assert.False(highlighted[0].IsKeyboardSelected);
        Assert.True(highlighted[1].IsKeyboardSelected);
    }
}
