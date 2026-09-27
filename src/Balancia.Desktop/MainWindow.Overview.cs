using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Balancia.Core;
using Balancia.Storage;

namespace Balancia.Desktop;

public partial class MainWindow
{
    private Grid? overviewLayout;
    private TextBlock? overviewSummaryLabel;
    private TextBlock? overviewStatus;
    private TextBlock? overviewIncomeValue;
    private TextBlock? overviewIncomeScope;
    private TextBlock? overviewExpensesValue;
    private TextBlock? overviewExpensesScope;
    private TextBlock? overviewAverageIncomeValue;
    private TextBlock? overviewAverageIncomeScope;
    private TextBlock? overviewAverageExpensesValue;
    private TextBlock? overviewAverageExpensesScope;
    private StackPanel? overviewCategoryBody;
    private TextBlock? overviewCategoryHeading;
    private IReadOnlyList<CategoryTotal> renderedOverviewCategories = [];
    private string? renderedOverviewCategoryId;

    private void RenderOverview(LedgerSnapshot ledgerSnapshot)
    {
        var label = OverviewScopeLabel();
        overviewFilterFrom = null;
        overviewFilterTo = null;
        renderedPeriodState = null;
        var layout = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*"),
            RowSpacing = 11
        };
        var overviewHeader = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto")
        };
        overviewSummaryLabel = QuietText($"{label}", 12);
        overviewSummaryLabel.Foreground = Brush.Parse("#FFB95D4D");
        overviewSummaryLabel.VerticalAlignment = VerticalAlignment.Center;
        AddColumn(overviewHeader, overviewSummaryLabel, 0);
        overviewStatus = QuietText(Status.Text ?? string.Empty, 11);
        overviewStatus.HorizontalAlignment = HorizontalAlignment.Right;
        overviewStatus.VerticalAlignment = VerticalAlignment.Center;
        overviewStatus.Margin = new Thickness(0, 0, 8, 0);
        AddColumn(overviewHeader, overviewStatus, 1);
        var settings = IconButton("⚙", "Open settings", OpenSettingsDialog);
        settings.VerticalAlignment = VerticalAlignment.Center;
        AddColumn(overviewHeader, settings, 2);
        AddRow(layout, overviewHeader, 0);
        overviewFiltersVisible = true;
        AddRow(layout, OverviewFilterPanel(), 1);

        var accountPanel = AccountSummaryPanel(ledgerSnapshot);
        var (incomeBorder, incomeValue, incomeScope) = Metric("Income", ledgerSnapshot.MonthlyIncome, label, "#2C8B6D");
        var (expensesBorder, expensesValue, expensesScope) = Metric("Expenses", ledgerSnapshot.MonthlyExpenses, label, "#B95D4D");
        overviewIncomeValue = incomeValue;
        overviewIncomeScope = incomeScope;
        overviewExpensesValue = expensesValue;
        overviewExpensesScope = expensesScope;
        var averageScope = OverviewAverageScopeLabel();
        var (averageIncomeBorder, averageIncomeValue, averageIncomeScope) =
            Metric("Average", overviewAverages.Income, averageScope, "#2C8B6D");
        var (averageExpensesBorder, averageExpensesValue, averageExpensesScope) =
            Metric("Average", overviewAverages.Expenses, averageScope, "#B95D4D");
        overviewAverageIncomeValue = averageIncomeValue;
        overviewAverageIncomeScope = averageIncomeScope;
        overviewAverageExpensesValue = averageExpensesValue;
        overviewAverageExpensesScope = averageExpensesScope;
        var dashboard = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("0.6*,0.4*,0.4*,1.45*,1.45*"),
            RowDefinitions = new RowDefinitions("Auto,*"),
            RowSpacing = 11,
            ColumnSpacing = 11
        };
        AddColumn(dashboard, accountPanel, 0);
        var incomeExpenses = new Grid
        {
            RowDefinitions = new RowDefinitions("*,*"),
            RowSpacing = 6
        };
        AddRow(incomeExpenses, incomeBorder, 0);
        AddRow(incomeExpenses, expensesBorder, 1);
        AddColumn(dashboard, incomeExpenses, 1);
        var averages = new Grid
        {
            RowDefinitions = new RowDefinitions("*,*"),
            RowSpacing = 6
        };
        AddRow(averages, averageIncomeBorder, 0);
        AddRow(averages, averageExpensesBorder, 1);
        AddColumn(dashboard, averages, 2);
        AddColumn(dashboard, TrendPanel(), 3);
        AddColumn(dashboard, TimelinePanel(), 4);

        var categoryPanel = CategoryManagementPanel(ledgerSnapshot);
        var historyPanel = HistoryPanel();
        var rightColumn = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            RowSpacing = 11
        };
        AddRow(rightColumn, CategoriesPanel(ledgerSnapshot), 0);
        AddRow(rightColumn, RemindersPanel(), 1);
        var historyAndRightPanels = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("2.4*,1*"),
            ColumnSpacing = 11
        };
        AddColumn(historyAndRightPanels, historyPanel, 0);
        AddColumn(historyAndRightPanels, rightColumn, 1);
        var lowerPanels = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("0.72*,3.58*"),
            ColumnSpacing = 11
        };
        AddColumn(lowerPanels, categoryPanel, 0);
        AddColumn(lowerPanels, historyAndRightPanels, 1);
        AddRow(dashboard, lowerPanels, 1);
        Grid.SetColumnSpan(lowerPanels, 5);
        AddRow(layout, dashboard, 2);
        overviewLayout = layout;
        ResponsiveBody.Content = layout;
        UpdateAnalyticsCharts();
    }

    private void UpdateOverviewInPlace()
    {
        if (snapshot is not { } ledgerSnapshot)
        {
            return;
        }

        var label = OverviewScopeLabel();
        overviewSummaryLabel!.Text = $"{label}";
        overviewIncomeValue!.Text = AmountText(ledgerSnapshot.MonthlyIncome);
        overviewIncomeScope!.Text = label;
        overviewExpensesValue!.Text = AmountText(ledgerSnapshot.MonthlyExpenses);
        overviewExpensesScope!.Text = label;
        overviewAverageIncomeValue!.Text = AmountText(overviewAverages.Income);
        overviewAverageIncomeScope!.Text = OverviewAverageScopeLabel();
        overviewAverageExpensesValue!.Text = AmountText(overviewAverages.Expenses);
        overviewAverageExpensesScope!.Text = OverviewAverageScopeLabel();
        UpdateAnalyticsCharts();
        UpdateOverviewPeriodButtons();
        if (renderedOverviewCategoryId != overviewFilter.CategoryId ||
            !renderedOverviewCategories.SequenceEqual(ledgerSnapshot.LargestCategories))
        {
            FillCategoriesPanel(overviewCategoryBody!, ledgerSnapshot);
        }

        var total = overviewHistory?.TotalCount ?? 0;
        var first = total == 0 ? 0 : overviewOffset + 1;
        var last = overviewOffset + (overviewHistory?.Hits.Count ?? 0);
        overviewHistoryHeading!.Text = $"Transactions · {first:N0}-{last:N0} / {total:N0}";
        overviewHistoryEmpty!.IsVisible = total == 0;
        var currentItems = overviewHistoryList!.ItemsSource?.OfType<HistoryItem>().ToArray() ?? [];
        var nextItems = overviewHistory?.Hits.Select(hit => new HistoryItem(hit)).ToArray() ?? [];
        if (!currentItems.SequenceEqual(nextItems))
        {
            var selectedId = (overviewHistoryList.SelectedItem as HistoryItem)?.Hit.Entry.Id;
            overviewHistoryList.ItemsSource = nextItems;
            overviewHistoryList.SelectedItem = nextItems.FirstOrDefault(item => item.Hit.Entry.Id == selectedId);
        }
        overviewPreviousPage!.IsEnabled = overviewOffset > 0;
        overviewNextPage!.IsEnabled = overviewHistory is { } currentPage &&
            overviewOffset + currentPage.Hits.Count < currentPage.TotalCount;
    }

    private static (Border Border, TextBlock Value, TextBlock Scope) Metric(string title, Money value, string scope, string valueColor)
    {
        var valueText = new TextBlock
        {
            Text = AmountText(value),
            FontSize = 23,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush.Parse(valueColor)
        };
        var scopeText = QuietText(scope, 11);
        var titleText = Heading(title, 11);
        titleText.Margin = new Thickness(0, 0, 0, 2);
        valueText.VerticalAlignment = VerticalAlignment.Center;
        var content = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto")
        };
        AddRow(content, titleText, 0);
        AddRow(content, valueText, 1);
        AddRow(content, scopeText, 2);
        var border = Panel(content);
        border.Padding = new Thickness(15, 6);
        return (border, valueText, scopeText);
    }

}
