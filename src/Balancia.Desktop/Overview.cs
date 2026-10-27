using Avalonia.Controls;
using Balancia.Core;
using static Balancia.Desktop.InputParsing;
using static Balancia.Desktop.Localization.UiText;

namespace Balancia.Desktop;

public partial class MainWindow
{
    private void LocalizeOverview()
    {
        ToolTip.SetTip(SettingsButton, Get("Open settings"));
        IncomeCard.Title = Get("Income");
        ExpensesCard.Title = Get("Expenses");
        AverageIncomeCard.Title = Get("Average");
        AverageExpensesCard.Title = Get("Average");
        LocalizeFilters();
        LocalizeTransactionsPanel();
        RefreshTrendIntervalLabel();
    }

    private void RenderOverview(LedgerSnapshot ledgerSnapshot)
    {
        RenderSummaryCards(ledgerSnapshot);
        UpdateAnalyticsCharts();
        UpdateFilterButtons();
        RenderCategories(ledgerSnapshot);
        RenderTransactions();
    }

    private void RenderSummaryCards(LedgerSnapshot ledgerSnapshot)
    {
        var scope = Get(ScopeLabel());
        var averageScope = Get(AverageScopeLabel());
        SummaryLabel.Text = scope;
        IncomeCard.Value = AmountText(ledgerSnapshot.MonthlyIncome);
        IncomeCard.Scope = scope;
        ExpensesCard.Value = AmountText(ledgerSnapshot.MonthlyExpenses);
        ExpensesCard.Scope = scope;
        AverageIncomeCard.Value = AmountText(overview!.Averages.Income);
        AverageIncomeCard.Scope = averageScope;
        AverageExpensesCard.Value = AmountText(overview!.Averages.Expenses);
        AverageExpensesCard.Scope = averageScope;
    }

    private void RenderCategories(LedgerSnapshot ledgerSnapshot)
    {
        if (renderedCategoryId == filter.CategoryId &&
            renderedCategories.SequenceEqual(ledgerSnapshot.LargestCategories))
        {
            return;
        }

        FillCategoriesPanel(CategoryBody, ledgerSnapshot);
    }

    private void RenderTransactions()
    {
        var page = overview?.Transactions;
        var total = page?.TotalCount ?? 0;
        var shown = page?.Hits.Count ?? 0;
        var first = total == 0 ? 0 : offset + 1;
        TransactionsHeading.Text = $"{Get("Transactions")} · {first:N0}-{offset + shown:N0} / {total:N0}";
        TransactionsEmpty.IsVisible = total == 0;

        DateOnly? previousDate = null;
        var alternateDay = false;
        var nextItems = page?.Hits.Select(hit =>
        {
            var date = hit.Entry.Draft.Date;
            if (previousDate is not null && previousDate != date)
            {
                alternateDay = !alternateDay;
            }
            previousDate = date;
            return new TransactionItem(hit, alternateDay);
        }).ToArray() ?? [];
        var currentItems = TransactionsList.ItemsSource?.OfType<TransactionItem>() ?? [];
        if (!currentItems.SequenceEqual(nextItems))
        {
            var selectedId = (TransactionsList.SelectedItem as TransactionItem)?.Hit.Entry.Id;
            TransactionsList.ItemsSource = nextItems;
            TransactionsList.SelectedItem = nextItems.FirstOrDefault(item => item.Hit.Entry.Id == selectedId);
        }

        TransactionsPreviousPage.IsEnabled = offset > 0;
        TransactionsNextPage.IsEnabled = offset + shown < total;
    }
}
