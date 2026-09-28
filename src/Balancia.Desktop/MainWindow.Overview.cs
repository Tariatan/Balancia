using Avalonia.Controls;
using Balancia.Core;
using static Balancia.Desktop.Localization.UiText;

namespace Balancia.Desktop;

public partial class MainWindow
{
    private IReadOnlyList<CategoryTotal> renderedOverviewCategories = [];
    private string? renderedOverviewCategoryId;

    private void LocalizeOverview()
    {
        ToolTip.SetTip(overviewSettingsButton, Get("Open settings"));
        overviewIncomeTitle.Text = Get("Income");
        overviewExpensesTitle.Text = Get("Expenses");
        overviewAverageIncomeTitle.Text = Get("Average");
        overviewAverageExpensesTitle.Text = Get("Average");
        LocalizeOverviewFilters();
        LocalizeHistoryPanel();
        LocalizeTrendPanel();
    }

    private void RenderOverview(LedgerSnapshot ledgerSnapshot)
    {
        var label = OverviewScopeLabel();
        overviewSummaryLabel.Text = Get(label);
        overviewIncomeValue.Text = AmountText(ledgerSnapshot.MonthlyIncome);
        overviewIncomeScope.Text = Get(label);
        overviewExpensesValue.Text = AmountText(ledgerSnapshot.MonthlyExpenses);
        overviewExpensesScope.Text = Get(label);
        overviewAverageIncomeValue.Text = AmountText(overviewAverages.Income);
        overviewAverageIncomeScope.Text = Get(OverviewAverageScopeLabel());
        overviewAverageExpensesValue.Text = AmountText(overviewAverages.Expenses);
        overviewAverageExpensesScope.Text = Get(OverviewAverageScopeLabel());
        UpdateAnalyticsCharts();
        UpdateOverviewPeriodButtons();
        if (renderedOverviewCategoryId != overviewFilter.CategoryId ||
            !renderedOverviewCategories.SequenceEqual(ledgerSnapshot.LargestCategories))
        {
            FillCategoriesPanel(overviewCategoryBody, ledgerSnapshot);
        }

        var total = overviewHistory?.TotalCount ?? 0;
        var first = total == 0 ? 0 : overviewOffset + 1;
        var last = overviewOffset + (overviewHistory?.Hits.Count ?? 0);
        overviewHistoryHeading.Text = $"{Get("Transactions")} · {first:N0}-{last:N0} / {total:N0}";
        overviewHistoryEmpty.IsVisible = total == 0;
        var currentItems = overviewHistoryList.ItemsSource?.OfType<HistoryItem>().ToArray() ?? [];
        var nextItems = overviewHistory?.Hits.Select(hit => new HistoryItem(hit)).ToArray() ?? [];
        if (!currentItems.SequenceEqual(nextItems))
        {
            var selectedId = (overviewHistoryList.SelectedItem as HistoryItem)?.Hit.Entry.Id;
            overviewHistoryList.ItemsSource = nextItems;
            overviewHistoryList.SelectedItem = nextItems.FirstOrDefault(item => item.Hit.Entry.Id == selectedId);
        }
        overviewPreviousPage.IsEnabled = overviewOffset > 0;
        overviewNextPage.IsEnabled = overviewHistory is { } currentPage &&
            overviewOffset + currentPage.Hits.Count < currentPage.TotalCount;
    }
}
