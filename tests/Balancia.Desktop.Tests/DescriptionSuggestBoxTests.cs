using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Xunit;

namespace Balancia.Desktop.Tests;

public sealed class DescriptionSuggestBoxTests
{
    private static readonly string[] History = ["Lunch at work", "Lunch with Anna", "Rent", "lunch at work", "Groceries"];

    private static (Window Window, TextBox Input, DescriptionSuggestBox Box, TextBox Other) Show(IEnumerable<string>? history = null)
    {
        var input = new TextBox();
        var box = new DescriptionSuggestBox(input, history ?? History);
        var other = new TextBox();
        var window = new Window { Width = 500, Height = 300, Content = new StackPanel { Children = { box, other } } };
        window.Show();
        input.Focus();
        return (window, input, box, other);
    }

    private static void Press(Window window, Key key, RawInputModifiers modifiers = RawInputModifiers.None) =>
        window.KeyPress(key, modifiers, PhysicalKey.None, null);

    /// <summary>Types into the box; the text box raises its change notification through the dispatcher, so the queued jobs run before the test looks.</summary>
    private static void Type(TextBox input, string text)
    {
        input.Text = text;
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void Typing_ThePrefixOfEarlierDescriptions_OffersThemWithoutDuplicates()
    {
        // Arrange
        var (_, input, box, _) = Show();

        // Act
        Type(input, "lun");

        // Assert
        Assert.Equal(["Lunch at work", "Lunch with Anna"], box.Matches);
        Assert.Equal(2, box.Rows.Count);
        Assert.True(box.IsOpen);
        Assert.Equal(0, box.SelectedIndex);
    }

    [AvaloniaTheory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("zzz")]
    public void Typing_NothingOrNoMatch_OffersNothingAndStaysClosed(string text)
    {
        // Arrange
        var (_, input, box, _) = Show();

        // Act
        Type(input, text);

        // Assert
        Assert.Empty(box.Matches);
        Assert.Empty(box.Rows);
        Assert.False(box.IsOpen);
    }

    [AvaloniaFact]
    public void Matches_AreLimitedToTwentyAndOnlyStartOfTheDescriptionCounts()
    {
        // Arrange
        var history = Enumerable.Range(0, 30).Select(i => $"Coffee {i:00}").Append("Iced coffee");
        var (_, input, box, _) = Show(history);

        // Act
        Type(input, "coffee");

        // Assert
        Assert.Equal(20, box.Matches.Count);
        Assert.DoesNotContain("Iced coffee", box.Matches);
    }

    [AvaloniaFact]
    public void Typing_WithoutFocus_FillsTheMatchesButDoesNotOpenThePopup()
    {
        // Arrange
        var (_, input, box, other) = Show();
        other.Focus();

        // Act
        Type(input, "lun");

        // Assert
        Assert.Equal(2, box.Matches.Count);
        Assert.False(box.IsOpen);
    }

    [AvaloniaFact]
    public void DownAndUp_MoveTheHighlightWithinTheMatches()
    {
        // Arrange
        var (window, input, box, _) = Show();
        Type(input, "lun");

        // Act and Assert
        Press(window, Key.Down);
        Assert.Equal(1, box.SelectedIndex);
        Press(window, Key.Down);
        Assert.Equal(1, box.SelectedIndex);
        Press(window, Key.Up);
        Press(window, Key.Up);
        Assert.Equal(0, box.SelectedIndex);
    }

    [AvaloniaFact]
    public void Tab_AcceptsTheHighlightedMatchAndClosesThePopup()
    {
        // Arrange
        var (window, input, box, _) = Show();
        Type(input, "lun");
        Press(window, Key.Down);

        // Act
        Press(window, Key.Tab);

        // Assert
        Assert.Equal("Lunch with Anna", input.Text);
        Assert.False(box.IsOpen);
    }

    [AvaloniaFact]
    public void ShiftTab_DoesNotAcceptAndTheFocusMovesOnWhichClosesThePopup()
    {
        // Arrange
        var (window, input, box, _) = Show();
        Type(input, "lun");

        // Act
        Press(window, Key.Tab, RawInputModifiers.Shift);

        // Assert
        Assert.Equal("lun", input.Text);
        Assert.False(input.IsFocused);
        Assert.False(box.IsOpen);
    }

    [AvaloniaFact]
    public void Escape_ClosesThePopupAndKeepsTheText()
    {
        // Arrange
        var (window, input, box, _) = Show();
        Type(input, "lun");

        // Act
        Press(window, Key.Escape);

        // Assert
        Assert.False(box.IsOpen);
        Assert.Equal("lun", input.Text);
    }

    [AvaloniaFact]
    public void Clicking_AMatch_AcceptsItAndDoesNotReopenThePopup()
    {
        // Arrange
        var (_, input, box, _) = Show();
        Type(input, "lun");

        // Act
        ((Button)box.Rows[1]).Press();

        // Assert
        Assert.Equal("Lunch with Anna", input.Text);
        Assert.False(box.IsOpen);
        Assert.Equal(["Lunch at work", "Lunch with Anna"], box.Matches);
    }

    [AvaloniaFact]
    public void LosingFocus_ClosesThePopup()
    {
        // Arrange
        var (_, input, box, other) = Show();
        Type(input, "lun");

        // Act
        other.Focus();

        // Assert
        Assert.False(box.IsOpen);
    }

    [AvaloniaFact]
    public void Keys_WhileThePopupIsClosed_AreLeftAlone()
    {
        // Arrange
        var (window, input, box, _) = Show();
        Type(input, "zzz");

        // Act
        Press(window, Key.Down);
        Press(window, Key.Tab);

        // Assert
        Assert.Equal(0, box.SelectedIndex);
        Assert.Equal("zzz", input.Text);
    }

    [AvaloniaFact]
    public void TheHighlightedRow_IsShadedAndTheOthersAreNot()
    {
        // Arrange
        var (window, input, box, _) = Show();
        Type(input, "lun");
        Press(window, Key.Down);

        // Act
        var first = ((Button)box.Rows[0]).Background;
        var second = ((Button)box.Rows[1]).Background;

        // Assert
        Assert.NotSame(first, second);
        Assert.Equal(Avalonia.Media.Brushes.Transparent, first);
    }
}
