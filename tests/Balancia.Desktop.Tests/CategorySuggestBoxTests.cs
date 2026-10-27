using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Balancia.Core;
using Xunit;

using static Balancia.Desktop.Localization.UiText;

namespace Balancia.Desktop.Tests;

public sealed class CategorySuggestBoxTests
{
    private static readonly string[] Paths = ["Food", "Food / Dinner", "Food / Lunch", "Travel", "Travel / Rail", "Lunch box"];
    private static readonly string[] Regular = ["Food / Lunch"];

    private static (Window Window, TextBox Input, CategorySuggestBox Box) Show(IReadOnlyList<string>? regular = null)
    {
        var input = new TextBox();
        var box = new CategorySuggestBox(input, Paths, regular ?? Regular);
        var window = new Window { Width = 500, Height = 300, Content = box };
        window.Show();
        input.Focus();
        return (window, input, box);
    }

    private static void Press(Window window, Key key) => window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);

    /// <summary>Types into the box; the text box raises its change notification through the dispatcher, so the queued jobs run before the test looks.</summary>
    private static void Type(TextBox input, string text)
    {
        input.Text = text;
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void Typing_AQuery_RanksRegularFirstThenTheTopMatchThenTheRest()
    {
        // Arrange
        var (_, input, box) = Show();

        // Act
        Type(input, "food");

        // Assert
        Assert.Equal(["Food / Lunch", "Food", "Food / Dinner"], box.Suggestions.Select(s => s.Path));
        Assert.Equal([CategorySuggestionSection.Regular, CategorySuggestionSection.TopMatch, CategorySuggestionSection.Other],
            box.Suggestions.Select(s => s.Section));
        Assert.Equal(3, box.Rows.Count);
        Assert.True(box.IsOpen);
    }

    [AvaloniaFact]
    public void Typing_AQuery_HighlightsTheFirstSuggestion()
    {
        // Arrange
        var (_, input, box) = Show();

        // Act
        Type(input, "food");

        // Assert
        Assert.Equal(0, box.SelectedIndex);
        Assert.Equal([true, false, false], box.Suggestions.Select(s => s.IsKeyboardSelected));
    }

    [AvaloniaTheory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("zzz")]
    public void Typing_NothingOrNoMatch_OffersNothingAndStaysClosed(string text)
    {
        // Arrange
        var (_, input, box) = Show();
        Type(input, "food");

        // Act
        Type(input, text);

        // Assert
        Assert.Empty(box.Suggestions);
        Assert.Empty(box.Rows);
        Assert.Equal(-1, box.SelectedIndex);
        Assert.False(box.IsOpen);
    }

    [AvaloniaFact]
    public void DownAndUp_MoveTheHighlightAndClampAtBothEnds()
    {
        // Arrange
        var (window, input, box) = Show();
        Type(input, "food");

        // Act and Assert
        Press(window, Key.Down);
        Assert.Equal(1, box.SelectedIndex);
        Assert.Equal([false, true, false], box.Suggestions.Select(s => s.IsKeyboardSelected));
        Press(window, Key.Down);
        Press(window, Key.Down);
        Assert.Equal(2, box.SelectedIndex);
        Press(window, Key.Up);
        Press(window, Key.Up);
        Press(window, Key.Up);
        Assert.Equal(0, box.SelectedIndex);
        Assert.True(box.IsOpen);
    }

    [AvaloniaFact]
    public void Tab_AcceptsTheHighlightedSuggestionClosesThePopupAndDoesNotReopenIt()
    {
        // Arrange
        var (window, input, box) = Show();
        Type(input, "food");
        Press(window, Key.Down);

        // Act
        Press(window, Key.Tab);
        Dispatcher.UIThread.RunJobs();

        // Assert
        Assert.Equal("Food", input.Text);
        Assert.False(box.IsOpen);
    }

    [AvaloniaFact]
    public void Tab_WithoutSuggestions_IsLeftAlone()
    {
        // Arrange
        var (window, input, box) = Show();
        Type(input, "zzz");

        // Act
        Press(window, Key.Tab);

        // Assert
        Assert.Equal("zzz", input.Text);
        Assert.False(box.IsOpen);
    }

    [AvaloniaFact]
    public void Clicking_ASuggestion_AcceptsItAndClosesThePopup()
    {
        // Arrange
        var (_, input, box) = Show();
        Type(input, "food");

        // Act
        ((Button)box.Rows[2]).Press();
        Dispatcher.UIThread.RunJobs();

        // Assert
        Assert.Equal("Food / Dinner", input.Text);
        Assert.False(box.IsOpen);
    }

    [AvaloniaFact]
    public void Typing_AfterAcceptingASuggestion_OffersSuggestionsAgain()
    {
        // Arrange
        var (_, input, box) = Show();
        Type(input, "food");
        ((Button)box.Rows[1]).Press();
        Dispatcher.UIThread.RunJobs();

        // Act
        Type(input, "trav");

        // Assert
        Assert.Equal(["Travel", "Travel / Rail"], box.Suggestions.Select(s => s.Path));
        Assert.True(box.IsOpen);
    }

    [AvaloniaFact]
    public void Rows_ShowASectionHeaderOnlyWhereASectionStartsAndMarkTheHighlightedRow()
    {
        // Arrange
        var (_, input, box) = Show();

        // Act
        Type(input, "food");

        // Assert
        static StackPanel Content(Control row) => (StackPanel)((Button)row).Content!;
        Assert.Equal(2, Content(box.Rows[0]).Children.Count);
        Assert.Equal(Get("REGULAR"), ((TextBlock)Content(box.Rows[0]).Children[0]).Text);
        Assert.Equal(Get("TOP MATCH"), ((TextBlock)Content(box.Rows[1]).Children[0]).Text);
        Assert.Equal(Get("Category suggestion header"), ((TextBlock)Content(box.Rows[2]).Children[0]).Text);
        var highlighted = (Border)Content(box.Rows[0]).Children[1];
        var other = (Border)Content(box.Rows[1]).Children[1];
        Assert.NotEqual(Brushes.Transparent, highlighted.Background);
        Assert.Equal(Brushes.Transparent, other.Background);
    }

    [AvaloniaFact]
    public void Reset_ClearsTheBoxAndItsSuggestionsAndClosesThePopup()
    {
        // Arrange
        var (_, input, box) = Show();
        Type(input, "food");

        // Act
        box.Reset([]);

        // Assert
        Assert.Equal(string.Empty, input.Text);
        Assert.Empty(box.Suggestions);
        Assert.Empty(box.Rows);
        Assert.Equal(-1, box.SelectedIndex);
        Assert.False(box.IsOpen);
    }

    [AvaloniaFact]
    public void Reset_WithNewRegularPaths_RanksByThemFromThenOn()
    {
        // Arrange
        var (_, input, box) = Show();
        Type(input, "food");
        var before = box.Suggestions[0].Path;

        // Act
        box.Reset(["Food / Dinner"]);
        Type(input, "food");

        // Assert
        Assert.Equal("Food / Lunch", before);
        Assert.Equal("Food / Dinner", box.Suggestions[0].Path);
    }
}
