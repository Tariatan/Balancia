using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Balancia.Core;
using Xunit;

using static Balancia.Desktop.Localization.UiText;

namespace Balancia.Desktop.Tests;

public sealed class TopExpendituresCardTests
{
    private static CategoryTotal Total(string name, decimal francs) => new(name, Money.FromFrancs(francs));

    private static TopExpendituresCard Show()
    {
        var card = new TopExpendituresCard();
        new Window { Width = 420, Height = 300, Content = card }.Show();
        return card;
    }

    private static StackPanel Body(TopExpendituresCard card) => card.FindControl<StackPanel>("TopExpendituresBody")!;

    private static string? Heading(TopExpendituresCard card) => card.FindControl<TextBlock>("TopExpendituresHeading")!.Text;

    private static Grid[] Rows(TopExpendituresCard card) => [.. Body(card).Children.OfType<Grid>()];

    private static TextBlock NameOf(Grid row) => row.Children.OfType<TextBlock>().First();

    private static ProgressBar BarOf(Grid row) => row.Children.OfType<ProgressBar>().Single();

    private static TextBlock AmountOf(Grid row) => row.Children.OfType<TextBlock>().Last();

    [AvaloniaFact]
    public void Render_WithTotals_ShowsOneRowPerCategoryInOrderWithTheirAmounts()
    {
        // Arrange
        var card = Show();

        // Act
        card.Render([Total("Food", 500m), Total("Rent", 300m), Total("Gym", 40m)], null);

        // Assert
        var rows = Rows(card);
        Assert.Equal(["Food", "Rent", "Gym"], rows.Select(row => NameOf(row).Text));
        Assert.Equal(
            [InputParsing.AmountText(Money.FromFrancs(500m)), InputParsing.AmountText(Money.FromFrancs(300m)), InputParsing.AmountText(Money.FromFrancs(40m))],
            rows.Select(row => AmountOf(row).Text));
    }

    [AvaloniaFact]
    public void Render_Bars_AreScaledToTheLargestTotal()
    {
        // Arrange
        var card = Show();

        // Act
        card.Render([Total("Food", 500m), Total("Rent", 300m)], null);

        // Assert
        var bars = Rows(card).Select(BarOf).ToArray();
        Assert.All(bars, bar => Assert.Equal(50000d, bar.Maximum));
        Assert.Equal([50000d, 30000d], bars.Select(bar => bar.Value));
    }

    [AvaloniaFact]
    public void Render_WithoutAScope_ShowsThePlainHeading()
    {
        // Arrange
        var card = Show();

        // Act
        card.Render([Total("Food", 500m)], null);

        // Assert
        Assert.Equal(Get("Top expenditures"), Heading(card));
    }

    [AvaloniaFact]
    public void Render_WithAScope_NamesItInTheHeading()
    {
        // Arrange
        var card = Show();

        // Act
        card.Render([Total("Lunch", 400m)], "Food");

        // Assert
        Assert.Equal(Format("Top expenditures · {0}", "Food"), Heading(card));
        Assert.Contains("Food", Heading(card));
    }

    [AvaloniaFact]
    public void Render_WithoutTotals_ShowsOnlyTheEmptyMessage()
    {
        // Arrange
        var card = Show();

        // Act
        card.Render([], null);

        // Assert
        var message = Assert.IsType<TextBlock>(Assert.Single(Body(card).Children));
        Assert.Equal(Get("No matching expenses."), message.Text);
        Assert.Empty(Rows(card));
    }

    [AvaloniaFact]
    public void Render_Again_ReplacesTheRows()
    {
        // Arrange
        var card = Show();
        card.Render([Total("Food", 500m), Total("Rent", 300m)], null);
        var first = Rows(card)[0];

        // Act
        card.Render([Total("Gym", 40m)], null);

        // Assert
        Assert.Equal(["Gym"], Rows(card).Select(row => NameOf(row).Text));
        Assert.DoesNotContain(first, Body(card).Children);
    }

    [AvaloniaFact]
    public void Render_WithTheSameInputs_StillRedrawsForALanguageChange()
    {
        // Arrange
        var card = Show();
        card.Render([Total("Food", 500m)], null);
        var first = Rows(card)[0];

        // Act
        card.Render([Total("Food", 500m)], null);

        // Assert
        Assert.NotSame(first, Rows(card)[0]);
    }

    [AvaloniaFact]
    public void RenderIfChanged_FirstCall_Draws()
    {
        // Arrange
        var card = Show();

        // Act
        card.RenderIfChanged([Total("Food", 500m)], null);

        // Assert
        Assert.Equal(["Food"], Rows(card).Select(row => NameOf(row).Text));
    }

    [AvaloniaFact]
    public void RenderIfChanged_SameTotalsAndScope_KeepsTheExistingRows()
    {
        // Arrange
        var card = Show();
        card.Render([Total("Food", 500m), Total("Rent", 300m)], "Food");
        var first = Rows(card)[0];

        // Act
        card.RenderIfChanged([Total("Food", 500m), Total("Rent", 300m)], "Food");

        // Assert
        Assert.Same(first, Rows(card)[0]);
    }

    [AvaloniaFact]
    public void RenderIfChanged_ChangedTotals_Redraws()
    {
        // Arrange
        var card = Show();
        card.Render([Total("Food", 500m)], null);
        var first = Rows(card)[0];

        // Act
        card.RenderIfChanged([Total("Food", 510m)], null);

        // Assert
        Assert.NotSame(first, Rows(card)[0]);
        Assert.Equal(InputParsing.AmountText(Money.FromFrancs(510m)), AmountOf(Rows(card)[0]).Text);
    }

    [AvaloniaFact]
    public void RenderIfChanged_OnlyTheScopeChanged_UpdatesTheHeading()
    {
        // Arrange
        var card = Show();
        card.Render([Total("Lunch", 400m)], null);

        // Act
        card.RenderIfChanged([Total("Lunch", 400m)], "Food");

        // Assert
        Assert.Equal(Format("Top expenditures · {0}", "Food"), Heading(card));
    }
}
