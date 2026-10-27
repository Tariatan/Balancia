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
        FilterRow.Localize();
        TransactionsCard.Localize();
        RefreshTrendIntervalLabel();
    }

    private void RenderOverview(LedgerSnapshot ledgerSnapshot)
    {
        RenderSummaryCards(ledgerSnapshot);
        UpdateAnalyticsCharts();
        UpdateFilterButtons();
        TopExpendituresCard.RenderIfChanged(ledgerSnapshot.LargestCategories, TopExpendituresScope(ledgerSnapshot));
        TransactionsCard.Render(overview!.Transactions, overviewState.Offset);
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

    private string? TopExpendituresScope(LedgerSnapshot ledgerSnapshot) =>
        overviewState.FilteredParent(ledgerSnapshot.Categories)?.Name;
}
