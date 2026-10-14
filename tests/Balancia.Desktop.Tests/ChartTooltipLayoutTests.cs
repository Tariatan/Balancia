using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Balancia.Core;
using Xunit;

namespace Balancia.Desktop.Tests;

public sealed class ChartTooltipLayoutTests
{
    [AvaloniaFact]
    public void BuildTooltip_NextPoint_ReusesTooltipAndUpdatesContent()
    {
        // Arrange
        var first = new FlowChartPoint(0, "", "", [new Money(100)], "Expense", ["Current period"], ["01 Oct 2026"]);
        var second = new FlowChartPoint(1, "", "", [new Money(250)], "Expense", ["Current period"], ["02 Oct 2026"]);
        var chart = new FlowChart();
        chart.SetData([first, second], [new("Expense", "#FF0000")], null);
        var tooltip = Assert.IsType<ToolTip>(chart.BuildTooltip(first));
        var firstContent = Assert.IsType<ChartTooltipData>(tooltip.Content);

        // Act
        var nextTooltip = chart.BuildTooltip(second);

        // Assert
        Assert.Same(tooltip, nextTooltip);
        var nextContent = Assert.IsType<ChartTooltipData>(tooltip.Content);
        Assert.NotEqual(firstContent.Rows[0].Amount, nextContent.Rows[0].Amount);
        Assert.Equal("02 Oct 2026", nextContent.Rows[0].Date);
    }

    [AvaloniaFact]
    public void TimelineTooltip_LocalizedRows_AllCellsFitAndAmountsAlign()
    {
        // Arrange
        var point = new FlowChartPoint(0.5, "", "", [new Money(1477422), Money.Zero],
            "Expense", ["Current period", "Current period"], ["16 Oct 2026", "15 Sept 2026"]);
        var chart = new FlowChart();
        chart.SetData([point], [new("Current", "#FF0000"), new("Previous", "#808080")], null);
        var tooltip = Assert.IsType<ToolTip>(chart.BuildTooltip(point));
        var window = new Window { Content = tooltip, SizeToContent = SizeToContent.WidthAndHeight };

        try
        {
            // Act
            window.Show();
            window.UpdateLayout();
            var cells = tooltip.GetVisualDescendants().OfType<TextBlock>()
                .Where(block => !string.IsNullOrEmpty(block.Text) && block.IsVisible).ToArray();

            // Assert
            Assert.Equal(7, cells.Length);
            foreach (var cell in cells)
            {
                var origin = cell.TranslatePoint(new Point(), tooltip)!.Value;
                Assert.True(origin.X >= 0, cell.Text);
                Assert.True(origin.X + cell.Bounds.Width <= tooltip.Bounds.Width + 1, cell.Text);
                Assert.True(origin.Y >= 0, cell.Text);
                Assert.True(origin.Y + cell.Bounds.Height <= tooltip.Bounds.Height + 1, cell.Text);
            }

            var amounts = cells.Where(cell => Grid.GetColumn(cell) == 2).ToArray();
            var rightEdges = amounts.Select(cell => cell.TranslatePoint(new Point(), tooltip)!.Value.X + cell.Bounds.Width).ToArray();
            Assert.Equal(rightEdges[0], rightEdges[1], 1);
        }
        finally
        {
            window.Close();
        }
    }
}
