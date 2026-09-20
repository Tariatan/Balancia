using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Balancia.Core;
using Balancia.Storage;
using Microsoft.Data.Sqlite;

namespace Balancia.Desktop;

public partial class MainWindow
{
    private (DateOnly? From, DateOnly? To) OverviewRange() => _overviewPeriod switch
    {
        OverviewPeriod.All => (null, null),
        OverviewPeriod.ThisWeek => WeekRange(_displayDate),
        OverviewPeriod.ThisMonth => (new DateOnly(_displayDate.Year, _displayDate.Month, 1),
            new DateOnly(_displayDate.Year, _displayDate.Month, 1).AddMonths(1).AddDays(-1)),
        OverviewPeriod.ThisYear => (new DateOnly(_displayDate.Year, 1, 1), new DateOnly(_displayDate.Year, 12, 31)),
        _ => (_customFrom, _customTo)
    };

    private static (DateOnly From, DateOnly To) WeekRange(DateOnly date)
    {
        var daysSinceMonday = ((int)date.DayOfWeek + 6) % 7;
        var monday = date.AddDays(-daysSinceMonday);
        return (monday, monday.AddDays(6));
    }

    private string OverviewPeriodLabel() => _overviewPeriod switch
    {
        OverviewPeriod.All => "All dates",
        OverviewPeriod.ThisWeek => "This week",
        OverviewPeriod.ThisMonth => _displayDate.ToString("MMMM yyyy", CultureInfo.CurrentCulture),
        OverviewPeriod.ThisYear => _displayDate.Year.ToString(CultureInfo.CurrentCulture),
        _ => $"{_customFrom:dd MMM yyyy} – {_customTo:dd MMM yyyy}"
    };

    private void RenderOverview(LedgerSnapshot snapshot)
    {
        var label = OverviewPeriodLabel();
        var layout = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*"),
            RowSpacing = 11
        };
        AddRow(layout, QuietText($"{label} · All accounts", 12), 0);
        var filterBox = new StackPanel { Spacing = 8 };
        var filters = new WrapPanel();
        filters.Children.Add(QuietText("PERIOD", 11));
        foreach (var (period, title) in new[] { (OverviewPeriod.All, "All"), (OverviewPeriod.ThisWeek, "This Week"),
                     (OverviewPeriod.ThisMonth, "This Month"), (OverviewPeriod.ThisYear, "This Year"), (OverviewPeriod.Custom, "Custom") })
        {
            var button = ActionButton(title, async () =>
            {
                _overviewPeriod = period;
                if (period == OverviewPeriod.Custom && (_customFrom is null || _customTo is null))
                {
                    _customFrom = new DateOnly(_displayDate.Year, _displayDate.Month, 1);
                    _customTo = _displayDate;
                }
                await Run(Refresh);
            });
            button.Margin = new Thickness(5, 0, 0, 0);
            if (_overviewPeriod == period)
            {
                button.Background = Brush.Parse("#D8ECF3");
                button.Foreground = Brush.Parse("#1C627E");
                button.FontWeight = FontWeight.SemiBold;
            }
            filters.Children.Add(button);
        }
        filterBox.Children.Add(filters);
        if (_overviewPeriod == OverviewPeriod.Custom)
        {
            var from = DateInput(_customFrom);
            var to = DateInput(_customTo);
            from.Width = to.Width = 150;
            var customDates = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto"),
                ColumnSpacing = 10
            };
            AddColumn(customDates, Field("From", from), 0);
            AddColumn(customDates, Field("To", to), 1);
            var apply = ActionButton("Apply", async () =>
            {
                var first = from.SelectedDate is { } f ? DateOnly.FromDateTime(f.DateTime) : (DateOnly?)null;
                var last = to.SelectedDate is { } t ? DateOnly.FromDateTime(t.DateTime) : (DateOnly?)null;
                if (first is null || last is null || first > last)
                {
                    Status.Text = "Choose a valid From and To date.";
                    return;
                }
                _customFrom = first;
                _customTo = last;
                await Run(Refresh);
            });
            apply.VerticalAlignment = VerticalAlignment.Bottom;
            AddColumn(customDates, apply, 2);
            filterBox.Children.Add(customDates);
        }
        AddRow(layout, Panel(filterBox), 1);

        var summary = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("1.18*,*,*"),
            ColumnSpacing = 10
        };
        var accountRows = new StackPanel { Spacing = 0 };
        accountRows.Children.Add(Heading("NET WORTH · ALL ACCOUNTS", 11));
        foreach (var account in snapshot.Accounts)
        {
            accountRows.Children.Add(TwoColumn(account.ToString(), AmountText(account.Balance), 12,
                BalanceColor(account.Balance)));
        }

        if (snapshot.Accounts.Count == 0)
        {
            accountRows.Children.Add(QuietText("No accounts yet", 12));
        }

        var total = TwoColumn("Total net worth", AmountText(snapshot.NetWorth), 16,
            BalanceColor(snapshot.NetWorth));
        total.Margin = new Thickness(0, 9, 0, 0);
        accountRows.Children.Add(total);
        AddColumn(summary, Panel(accountRows), 0);
        AddColumn(summary, Metric("INCOME", snapshot.MonthlyIncome, label, "#2C8B6D"), 1);
        AddColumn(summary, Metric("EXPENSES", snapshot.MonthlyExpenses, label, "#B95D4D"), 2);
        AddRow(layout, summary, 2);

        var lower = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("1.7*,0.85*"),
            ColumnSpacing = 11
        };
        AddColumn(lower, HistoryPanel(), 0);
        var right = new StackPanel { Spacing = 11 };
        right.Children.Add(CategoriesPanel(snapshot));
        right.Children.Add(RemindersPanel());
        AddColumn(lower, right, 1);
        AddRow(layout, lower, 3);
        ResponsiveBody.Content = layout;
    }

    private static Border Metric(string title, Money value, string scope, string valueColor) => Panel(new StackPanel
    {
        Spacing = 18,
        Children =
        {
            Heading(title, 11), new TextBlock
            {
                Text = AmountText(value), FontSize = 23, FontWeight = FontWeight.SemiBold,
                Foreground = Brush.Parse(valueColor)
            },
            QuietText(scope, 11)
        }

    });

    private Border HistoryPanel()
    {
        var body = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*"),
            RowSpacing = 2
        };
        AddRow(body, SectionHeader($"Transaction history · {_overviewHistory?.TotalCount:N0}", "See all →", () => Navigate("Transactions")), 0);
        AddRow(body, HistoryRow("Date", "Description", "Category", "Account", "Amount", true, true), 1);

        if (_overviewHistory is not { Hits.Count: > 0 })
        {
            AddRow(body, QuietText("No transactions in this period.", 13), 2);
        }
        else
        {
            var entries = HistoryList(_overviewHistory.Hits, true);
            entries.DoubleTapped += async (_, _) =>
            {
                if (entries.SelectedItem is HistoryItem item)
                {
                    await OpenTransactionFromOverview(item.Hit.Entry);
                }
            };
            AddRow(body, entries, 2);
        }

        return Panel(body);
    }

    private static Grid SectionHeader(string title, string link, Func<Task> action)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(0, 0, 0, 10)
        };
        row.Children.Add(Heading(title, 13));
        var button = ActionButton(link, action);
        button.FontSize = 10;
        button.Padding = new Thickness(4);
        button.Background = Brushes.Transparent;
        button.Foreground = Brush.Parse("#3989A7");
        AddColumn(row, button, 1);
        return row;
    }
}
