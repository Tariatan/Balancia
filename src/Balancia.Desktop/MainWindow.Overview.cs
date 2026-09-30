using Avalonia.Controls;
using Balancia.Core;
using static Balancia.Desktop.Localization.UiText;

namespace Balancia.Desktop;

public partial class MainWindow
{
    private IReadOnlyList<CategoryTotal> renderedCategories = [];
    private string? renderedCategoryId;

    private void LocalizeOverview()
    {
        ToolTip.SetTip(SettingsButton, Get("Open settings"));
        IncomeTitle.Text = Get("Income");
        ExpensesTitle.Text = Get("Expenses");
        AverageIncomeTitle.Text = Get("Average");
        AverageExpensesTitle.Text = Get("Average");
        LocalizeFilters();
        LocalizeTransactionsPanel();
        LocalizeTrendPanel();
    }

    private void RenderOverview(LedgerSnapshot ledgerSnapshot)
    {
        var label = ScopeLabel();
        SummaryLabel.Text = Get(label);
        IncomeValue.Text = AmountText(ledgerSnapshot.MonthlyIncome);
        IncomeScope.Text = Get(label);
        ExpensesValue.Text = AmountText(ledgerSnapshot.MonthlyExpenses);
        ExpensesScope.Text = Get(label);
        AverageIncomeValue.Text = AmountText(averages.Income);
        AverageIncomeScope.Text = Get(AverageScopeLabel());
        AverageExpensesValue.Text = AmountText(averages.Expenses);
        AverageExpensesScope.Text = Get(AverageScopeLabel());
        UpdateAnalyticsCharts();
        UpdateFilterButtons();
        if (renderedCategoryId != filter.CategoryId ||
            !renderedCategories.SequenceEqual(ledgerSnapshot.LargestCategories))
        {
            FillCategoriesPanel(CategoryBody, ledgerSnapshot);
        }

        var total = transactions?.TotalCount ?? 0;
        var first = total == 0 ? 0 : offset + 1;
        var last = offset + (transactions?.Hits.Count ?? 0);
        TransactionsHeading.Text = $"{Get("Transactions")} · {first:N0}-{last:N0} / {total:N0}";
        TransactionsEmpty.IsVisible = total == 0;
        var currentItems = TransactionsList.ItemsSource?.OfType<TransactionItem>().ToArray() ?? [];
        var nextItems = transactions?.Hits.Select(hit => new TransactionItem(hit)).ToArray() ?? [];
        if (!currentItems.SequenceEqual(nextItems))
        {
            var selectedId = (TransactionsList.SelectedItem as TransactionItem)?.Hit.Entry.Id;
            TransactionsList.ItemsSource = nextItems;
            TransactionsList.SelectedItem = nextItems.FirstOrDefault(item => item.Hit.Entry.Id == selectedId);
        }
        TransactionsPreviousPage.IsEnabled = offset > 0;
        TransactionsNextPage.IsEnabled = transactions is { } currentPage &&
            offset + currentPage.Hits.Count < currentPage.TotalCount;
    }
}
