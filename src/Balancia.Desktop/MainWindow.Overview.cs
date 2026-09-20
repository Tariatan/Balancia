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
                     (OverviewPeriod.ThisMonth, "This Month"), (OverviewPeriod.ThisYear, "This Year"), (OverviewPeriod.Custom, "Filter") })
        {
            var button = ActionButton(title, async () =>
            {
                if (period == OverviewPeriod.Custom)
                {
                    _overviewFiltersVisible = !_overviewFiltersVisible;
                    await Run(Refresh);
                    return;
                }

            _overviewPeriod = period;
            _overviewOffset = 0;
                if (period == OverviewPeriod.Custom && (_customFrom is null || _customTo is null))
                {
                    _customFrom = new DateOnly(_displayDate.Year, _displayDate.Month, 1);
                    _customTo = _displayDate;
                }

                await Run(Refresh);
            });
            button.Margin = new Thickness(5, 0, 0, 0);
            if (_overviewPeriod == period || period == OverviewPeriod.Custom && _overviewFiltersVisible)
            {
                button.Background = Brush.Parse("#D8ECF3");
                button.Foreground = Brush.Parse("#1C627E");
                button.FontWeight = FontWeight.SemiBold;
            }
            filters.Children.Add(button);
        }
        filterBox.Children.Add(filters);
        if (_overviewFiltersVisible)
        {
            filterBox.Children.Add(OverviewFilterPanel());
        }
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
        var accountHeader = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(0, 0, 0, 7)
        };
        accountHeader.Children.Add(Heading("ACCOUNTS", 11));
        var accountActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 3
        };
        var accounts = new ListBox
        {
            ItemsSource = snapshot.Accounts.Select(a => new Choice<Account>(a, a.Name)).ToArray(),
            MinHeight = snapshot.Accounts.Count == 0 ? 0 : 45,
            MaxHeight = 170,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            ItemTemplate = new FuncDataTemplate<Choice<Account>>((choice, _) =>
            {
                var row = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                    MinHeight = 20
                };
                row.Children.Add(new TextBlock
                {
                    Text = choice.Value.Name,
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
                AddColumn(row, new TextBlock
                {
                    Text = AmountText(choice.Value.Balance),
                    FontSize = 12,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = BalanceColor(choice.Value.Balance),
                    VerticalAlignment = VerticalAlignment.Center
                }, 1);
                return row;
            }, true)
        };
        accounts.DoubleTapped += async (_, _) =>
        {
            if (accounts.SelectedItem is Choice<Account> selected)
            {
                await EditAccount(selected.Value);
            }
        };
        var addAccount = ActionButton("+", () => EditAccount(null));
        addAccount.Width = 30;
        addAccount.Padding = new Thickness(0);
        addAccount.FontSize = 18;
        addAccount.HorizontalContentAlignment = HorizontalAlignment.Center;

        var removeAccount = ActionButton("🗑", () => DeleteSelectedAccount(accounts));
        removeAccount.Width = 30;
        removeAccount.Padding = new Thickness(0);
        removeAccount.FontSize = 18;
        removeAccount.HorizontalContentAlignment = HorizontalAlignment.Center;

        var archiveAccount = ActionButton("▣", () => ArchiveSelectedAccount(accounts));
        archiveAccount.Width = 30;
        archiveAccount.Padding = new Thickness(0);
        archiveAccount.FontSize = 18;
        archiveAccount.HorizontalContentAlignment = HorizontalAlignment.Center;

        ToolTip.SetTip(addAccount, "Add account");
        ToolTip.SetTip(archiveAccount, "Archive selected account");
        ToolTip.SetTip(removeAccount, "Delete selected account");
        accountActions.Children.Add(addAccount);
        accountActions.Children.Add(archiveAccount);
        accountActions.Children.Add(removeAccount);
        AddColumn(accountHeader, accountActions, 1);
        accountRows.Children.Add(accountHeader);

        if (snapshot.Accounts.Count == 0)
        {
            accountRows.Children.Add(QuietText("No accounts yet", 12));
        }
        else
        {
            accountRows.Children.Add(accounts);
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
            ColumnDefinitions = new ColumnDefinitions("2.20*,1*"),
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

    private Border OverviewFilterPanel()
    {
        var search = Input(_overviewFilter.Description ?? "");
        search.Width = 880;
        search.HorizontalAlignment = HorizontalAlignment.Left;
        var accountChoices = new List<Choice<string?>> { new(null, "All accounts") };
        accountChoices.AddRange((_snapshot?.Accounts ?? []).Select(a => new Choice<string?>(a.Id, a.Name)));
        var account = new ComboBox
        {
            ItemsSource = accountChoices,
            SelectedItem = accountChoices.FirstOrDefault(c => c.Value == _overviewFilter.AccountId) ?? accountChoices[0],
            Width = 195
        };
        var types = new List<Choice<TransactionKind?>> { new(null, "All types") };
        types.AddRange(Enum.GetValues<TransactionKind>().Select(k => new Choice<TransactionKind?>(k, k.ToString())));
        var type = new ComboBox
        {
            ItemsSource = types,
            SelectedItem = types.FirstOrDefault(c => c.Value == _overviewFilter.Kind) ?? types[0],
            Width = 160
        };
        var categories = new List<Choice<string?>> { new(null, "All categories") };
        categories.AddRange((_snapshot?.Categories ?? []).Select(c => new Choice<string?>(c.Id, c.Path)));
        var category = new ComboBox
        {
            ItemsSource = categories,
            SelectedItem = categories.FirstOrDefault(c => c.Value == _overviewFilter.CategoryId) ?? categories[0],
            Width = 240
        };
        var from = DateInput(_overviewFilter.From);
        from.Width = 170;
        var to = DateInput(_overviewFilter.To);
        to.Width = 170;
        var minimum = Input(_overviewFilter.Minimum?.Francs.ToString("0.00", CultureInfo.InvariantCulture) ?? "");
        minimum.Width = 130;
        var maximum = Input(_overviewFilter.Maximum?.Francs.ToString("0.00", CultureInfo.InvariantCulture) ?? "");
        maximum.Width = 130;
        var filters = new StackPanel { Spacing = 10 };
        filters.Children.Add(Heading("Search and filters", 13));
        filters.Children.Add(Field("Description contains", search));
        filters.Children.Add(Row(Field("Account", account), Field("Type", type), Field("Category / subcategory", category)));
        filters.Children.Add(Row(Field("From", from), Field("To", to), Field("Min amount", minimum), Field("Max amount", maximum)));
        var apply = ActionButton("Apply filters", async () =>
        {
            _overviewFilter = new HistoryFilter(search.Text, ((Choice<string?>)account.SelectedItem!).Value,
                ((Choice<TransactionKind?>)type.SelectedItem!).Value, ((Choice<string?>)category.SelectedItem!).Value,
                from.SelectedDate is { } f ? DateOnly.FromDateTime(f.DateTime) : null,
                to.SelectedDate is { } t ? DateOnly.FromDateTime(t.DateTime) : null,
                OptionalMoney(minimum), OptionalMoney(maximum));
            _overviewOffset = 0;
            _overviewFiltersVisible = false;
            await Run(Refresh);
        });
        var clear = ActionButton("Clear filters", async () =>
        {
            _overviewFilter = new();
            await Run(Refresh);
        });
        filters.Children.Add(Row(apply, clear));

        return Panel(filters);
    }

    private static Border Metric(string title, Money value, string scope, string valueColor) => Panel(new StackPanel
    {
        Spacing = 18,
        Children =
        {
            Heading(title, 11),
            new TextBlock
            {
                Text = AmountText(value),
                FontSize = 23,
                FontWeight = FontWeight.SemiBold,
                Foreground = Brush.Parse(valueColor)
            },
            QuietText(scope, 11)
        }
    });

    private Border HistoryPanel()
    {
        var body = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
            RowSpacing = 2
        };
        ListBox? historyList = null;
        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(0, 0, 0, 10)
        };
        var total = _overviewHistory?.TotalCount ?? 0;
        var first = total == 0 ? 0 : _overviewOffset + 1;
        var last = _overviewOffset + (_overviewHistory?.Hits.Count ?? 0);
        header.Children.Add(Heading($"Transaction history · {first:N0}-{last:N0} / {total:N0}", 13));
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 3
        };
        var add = ActionButton("+", () => EditTransaction(null));
        add.Width = 30;
        add.Padding = new Thickness(0);
        add.FontSize = 18;
        add.HorizontalContentAlignment = HorizontalAlignment.Center;

        var remove = ActionButton("🗑", async () =>
        {
            if (historyList?.SelectedItem is HistoryItem item)
            {
                await RemoveTransaction(item.Hit.Entry);
            }
        });
        remove.Width = 30;
        remove.Padding = new Thickness(0);
        remove.FontSize = 18;
        remove.HorizontalContentAlignment = HorizontalAlignment.Center;

        ToolTip.SetTip(add, "Add transaction");
        ToolTip.SetTip(remove, "Delete selected transaction");
        actions.Children.Add(add);
        actions.Children.Add(remove);
        AddColumn(header, actions, 1);
        AddRow(body, header, 0);
        AddRow(body, HistoryRow("Date", "Description", "Category", "Account", "Amount", true), 1);

        if (_overviewHistory is not { Hits.Count: > 0 })
        {
            AddRow(body, QuietText("No transactions in this period.", 13), 2);
        }
        else
        {
            historyList = HistoryList(_overviewHistory.Hits);
            historyList.DoubleTapped += async (_, _) =>
            {
                if (historyList.SelectedItem is HistoryItem item)
                {
                    await EditTransaction(item.Hit.Entry);
                }
            };
            AddRow(body, historyList, 2);
        }

        var previous = ActionButton("Previous page", async () =>
        {
            _overviewOffset = Math.Max(0, _overviewOffset - HistoryPageSize);
            await Run(Refresh);
        });
        previous.IsEnabled = _overviewOffset > 0;
        var next = ActionButton("Next page", async () =>
        {
            _overviewOffset += HistoryPageSize;
            await Run(Refresh);
        });
        next.IsEnabled = _overviewHistory is { } page && _overviewOffset + page.Hits.Count < page.TotalCount;
        AddRow(body, Row(previous, next), 3);

        var panel = Panel(body);
        panel.Padding = new Thickness(15, 15, 15, 5);
        return panel;
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
