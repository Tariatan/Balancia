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
    private Grid? _overviewLayout;
    private StackPanel? _overviewFilterBox;
    private Border? _overviewFilterCard;
    private readonly Dictionary<OverviewPeriod, Button> _overviewPeriodButtons = new();
    private (OverviewPeriod Period, bool FiltersVisible)? _renderedPeriodState;
    private TextBlock? _overviewSummaryLabel;
    private TextBlock? _overviewIncomeValue;
    private TextBlock? _overviewIncomeScope;
    private TextBlock? _overviewExpensesValue;
    private TextBlock? _overviewExpensesScope;
    private StackPanel? _overviewCategoryBody;
    private TextBlock? _overviewCategoryHeading;
    private IReadOnlyList<CategoryTotal> _renderedOverviewCategories = [];
    private string? _renderedOverviewCategoryId;
    private TextBlock? _overviewHistoryHeading;
    private ListBox? _overviewHistoryList;
    private TextBlock? _overviewHistoryEmpty;
    private Button? _overviewPreviousPage;
    private Button? _overviewNextPage;
    private CalendarDatePicker? _overviewFilterFrom;
    private CalendarDatePicker? _overviewFilterTo;
    private bool _updatingFilterControls;

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

    private string OverviewScopeLabel()
    {
        var range = OverviewRange();
        var hasAdditionalFilter = !string.IsNullOrEmpty(_overviewFilter.Description) ||
            _overviewFilter.AccountId is not null || _overviewFilter.Kind is not null ||
            _overviewFilter.CategoryId is not null || _overviewFilter.Minimum is not null ||
            _overviewFilter.Maximum is not null ||
            (_overviewFilter.From ?? range.From) != range.From ||
            (_overviewFilter.To ?? range.To) != range.To;
        return hasAdditionalFilter ? "Filtered transactions" : OverviewPeriodLabel();
    }

    private void RenderOverview(LedgerSnapshot snapshot)
    {
        var label = OverviewScopeLabel();
        _overviewFilterCard = null;
        _overviewFilterFrom = null;
        _overviewFilterTo = null;
        _renderedPeriodState = null;
        var layout = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*"),
            RowSpacing = 11
        };
        _overviewSummaryLabel = QuietText($"{label} · All accounts", 12);
        AddRow(layout, _overviewSummaryLabel, 0);
        var filterBox = new StackPanel { Spacing = 8 };
        _overviewFilterBox = filterBox;
        _overviewPeriodButtons.Clear();
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
                    UpdateOverviewFilterVisibility();
                    return;
                }

            _overviewPeriod = period;
            _overviewOffset = 0;
            if (period == OverviewPeriod.Custom && (_customFrom is null || _customTo is null))
            {
                _customFrom = new DateOnly(_displayDate.Year, _displayDate.Month, 1);
                _customTo = _displayDate;
            }

            // Keep the advanced filter fields in sync with the selected period
            // shortcut so the active date range is visible when Filter opens.
            var range = OverviewRange();
            _overviewFilter = _overviewFilter with { From = range.From, To = range.To };

            SyncOverviewFilterDates();
            await RequestOverviewFilterRefresh();
            });
            button.Margin = new Thickness(5, 0, 0, 0);
            if (_overviewPeriod == period || period == OverviewPeriod.Custom && _overviewFiltersVisible)
            {
                button.Background = Brush.Parse("#D8ECF3");
                button.Foreground = Brush.Parse("#1C627E");
                button.FontWeight = FontWeight.SemiBold;
            }
            filters.Children.Add(button);
            _overviewPeriodButtons.Add(period, button);
        }
        filterBox.Children.Add(filters);
        UpdateOverviewFilterVisibility();
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
                var first = from.SelectedDate is { } ? ParseDate(from) : (DateOnly?)null;
                var last = to.SelectedDate is { } ? ParseDate(to) : (DateOnly?)null;
                if (first is null || last is null || first > last)
                {
                    Status.Text = "Choose a valid From and To date.";
                    return;
                }
                _customFrom = first;
                _customTo = last;
                _overviewFilter = _overviewFilter with { From = first, To = last };
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
        var income = Metric("INCOME", snapshot.MonthlyIncome, label, "#2C8B6D");
        var expenses = Metric("EXPENSES", snapshot.MonthlyExpenses, label, "#B95D4D");
        var incomeBody = (StackPanel)income.Child!;
        var expensesBody = (StackPanel)expenses.Child!;
        _overviewIncomeValue = (TextBlock)incomeBody.Children[1];
        _overviewIncomeScope = (TextBlock)incomeBody.Children[2];
        _overviewExpensesValue = (TextBlock)expensesBody.Children[1];
        _overviewExpensesScope = (TextBlock)expensesBody.Children[2];
        AddColumn(summary, income, 1);
        AddColumn(summary, expenses, 2);
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
        _overviewLayout = layout;
        ResponsiveBody.Content = layout;
    }

    private void UpdateOverviewFilterVisibility()
    {
        if (_overviewFilterBox is null)
        {
            return;
        }

        if (_overviewFiltersVisible)
        {
            _overviewFilterCard ??= OverviewFilterPanel();
            if (!_overviewFilterBox.Children.Contains(_overviewFilterCard))
            {
                _overviewFilterBox.Children.Add(_overviewFilterCard);
            }
        }
        else if (_overviewFilterCard is not null)
        {
            _updatingFilterControls = true;
            try
            {
                _overviewFilterBox.Children.Remove(_overviewFilterCard);
            }
            finally
            {
                _updatingFilterControls = false;
            }
        }

        UpdateOverviewPeriodButtons();
    }

    private void UpdateOverviewPeriodButtons()
    {
        var current = (_overviewPeriod, _overviewFiltersVisible);
        if (_renderedPeriodState == current)
        {
            return;
        }

        foreach (var (period, button) in _overviewPeriodButtons)
        {
            var selected = period == _overviewPeriod ||
                period == OverviewPeriod.Custom && _overviewFiltersVisible;
            if (selected)
            {
                button.Background = Brush.Parse("#D8ECF3");
                button.Foreground = Brush.Parse("#1C627E");
                button.FontWeight = FontWeight.SemiBold;
            }
            else
            {
                button.ClearValue(Button.BackgroundProperty);
                button.ClearValue(Button.ForegroundProperty);
                button.ClearValue(Button.FontWeightProperty);
            }
        }

        _renderedPeriodState = current;
    }

    private void SyncOverviewFilterDates()
    {
        var range = OverviewRange();
        _updatingFilterControls = true;
        try
        {
            if (_overviewFilterFrom is not null)
            {
                _overviewFilterFrom.SelectedDate = (_overviewFilter.From ?? range.From)?.ToDateTime(TimeOnly.MinValue);
            }

            if (_overviewFilterTo is not null)
            {
                _overviewFilterTo.SelectedDate = (_overviewFilter.To ?? range.To)?.ToDateTime(TimeOnly.MinValue);
            }
        }
        finally
        {
            _updatingFilterControls = false;
        }
    }

    private void UpdateOverviewInPlace()
    {
        if (_snapshot is not { } snapshot)
        {
            return;
        }

        var label = OverviewScopeLabel();
        _overviewSummaryLabel!.Text = $"{label} · All accounts";
        _overviewIncomeValue!.Text = AmountText(snapshot.MonthlyIncome);
        _overviewIncomeScope!.Text = label;
        _overviewExpensesValue!.Text = AmountText(snapshot.MonthlyExpenses);
        _overviewExpensesScope!.Text = label;
        UpdateOverviewPeriodButtons();
        if (_renderedOverviewCategoryId != _overviewFilter.CategoryId ||
            !_renderedOverviewCategories.SequenceEqual(snapshot.LargestCategories))
        {
            FillCategoriesPanel(_overviewCategoryBody!, snapshot);
        }

        var total = _overviewHistory?.TotalCount ?? 0;
        var first = total == 0 ? 0 : _overviewOffset + 1;
        var last = _overviewOffset + (_overviewHistory?.Hits.Count ?? 0);
        _overviewHistoryHeading!.Text = $"Transaction history · {first:N0}-{last:N0} / {total:N0}";
        _overviewHistoryEmpty!.IsVisible = total == 0;
        var currentItems = _overviewHistoryList!.ItemsSource?.OfType<HistoryItem>().ToArray() ?? [];
        var nextItems = _overviewHistory?.Hits.Select(hit => new HistoryItem(hit)).ToArray() ?? [];
        if (!currentItems.SequenceEqual(nextItems))
        {
            var selectedId = (_overviewHistoryList.SelectedItem as HistoryItem)?.Hit.Entry.Id;
            _overviewHistoryList.ItemsSource = nextItems;
            _overviewHistoryList.SelectedItem = nextItems.FirstOrDefault(item => item.Hit.Entry.Id == selectedId);
        }
        _overviewPreviousPage!.IsEnabled = _overviewOffset > 0;
        _overviewNextPage!.IsEnabled = _overviewHistory is { } page &&
            _overviewOffset + page.Hits.Count < page.TotalCount;
    }

    private Border OverviewFilterPanel()
    {
        var activeFilter = _overviewFilter with
        {
            From = _overviewFilter.From ?? OverviewRange().From,
            To = _overviewFilter.To ?? OverviewRange().To
        };
        var search = Input(activeFilter.Description ?? "");
        search.Width = 300;
        search.HorizontalAlignment = HorizontalAlignment.Left;
        var accountChoices = new List<Choice<string?>> { new(null, "All accounts") };
        accountChoices.AddRange((_snapshot?.Accounts ?? []).Select(a => new Choice<string?>(a.Id, a.Name)));
        var account = new ComboBox
        {
            ItemsSource = accountChoices,
            SelectedItem = accountChoices.FirstOrDefault(c => c.Value == activeFilter.AccountId) ?? accountChoices[0],
            Width = 195
        };
        var types = new List<Choice<TransactionKind?>> { new(null, "All types") };
        types.AddRange(Enum.GetValues<TransactionKind>().Select(k => new Choice<TransactionKind?>(k, k.ToString())));
        var type = new ComboBox
        {
            ItemsSource = types,
            SelectedItem = types.FirstOrDefault(c => c.Value == activeFilter.Kind) ?? types[0],
            Width = 160
        };
        var categories = new List<Choice<string?>> { new(null, "All categories") };
        categories.AddRange((_snapshot?.Categories ?? []).Select(c => new Choice<string?>(c.Id, c.Path)));
        var category = new ComboBox
        {
            ItemsSource = categories,
            SelectedItem = categories.FirstOrDefault(c => c.Value == activeFilter.CategoryId) ?? categories[0],
            Width = 240
        };
        var from = DateInput(activeFilter.From);
        from.Width = 170;
        var to = DateInput(activeFilter.To);
        to.Width = 170;
        _overviewFilterFrom = from;
        _overviewFilterTo = to;
        var minimum = Input(activeFilter.Minimum?.Francs.ToString("0.00", CultureInfo.InvariantCulture) ?? "");
        minimum.Width = 130;
        var maximum = Input(activeFilter.Maximum?.Francs.ToString("0.00", CultureInfo.InvariantCulture) ?? "");
        maximum.Width = 130;
        async Task ApplyValues()
        {
            if (_updatingFilterControls || _overviewFilterFrom != from)
            {
                return;
            }

            var nextFilter = new HistoryFilter(search.Text, ((Choice<string?>)account.SelectedItem!).Value,
                ((Choice<TransactionKind?>)type.SelectedItem!).Value, ((Choice<string?>)category.SelectedItem!).Value,
                from.SelectedDate is { } ? ParseDate(from) : null,
                to.SelectedDate is { } ? ParseDate(to) : null,
                OptionalMoney(minimum), OptionalMoney(maximum));
            if (nextFilter == _overviewFilter)
            {
                return;
            }

            _overviewFilter = nextFilter;
            _overviewOffset = 0;
            await RequestOverviewFilterRefresh();
        }

        type.SelectionChanged += async (_, _) => await ApplyValues();
        account.SelectionChanged += async (_, _) => await ApplyValues();
        category.SelectionChanged += async (_, _) => await ApplyValues();
        // Apply after the calendar closes, rather than on every intermediate
        // SelectedDateChanged event while the popup is navigating.
        void ApplyAfterCalendarClosed()
        {
            // CalendarClosed can precede the control's final SelectedDate
            // property update on the first click. Run after that UI event turn.
            Dispatcher.UIThread.Post(() => _ = ApplyValues());
        }

        from.CalendarClosed += (_, _) => ApplyAfterCalendarClosed();
        to.CalendarClosed += (_, _) => ApplyAfterCalendarClosed();
        from.LostFocus += async (_, _) => await ApplyValues();
        to.LostFocus += async (_, _) => await ApplyValues();
        search.LostFocus += async (_, _) => await ApplyValues();
        minimum.LostFocus += async (_, _) => await ApplyValues();
        maximum.LostFocus += async (_, _) => await ApplyValues();

        var title = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(0, 0, 0, 2)
        };
        title.Children.Add(Heading("Search and filters", 13));
        var clear = ActionButton("🗑", async () =>
        {
            _overviewFilter = new();
            _overviewOffset = 0;
            _updatingFilterControls = true;
            try
            {
                if (_overviewFilterCard is not null)
                {
                    _overviewFilterBox?.Children.Remove(_overviewFilterCard);
                    _overviewFilterCard = null;
                }

                _overviewFilterFrom = null;
                _overviewFilterTo = null;
                UpdateOverviewFilterVisibility();
            }
            finally
            {
                _updatingFilterControls = false;
            }

            await RequestOverviewFilterRefresh();
        });
        clear.Width = 30;
        clear.Padding = new Thickness(0);
        clear.FontSize = 16;
        clear.HorizontalContentAlignment = HorizontalAlignment.Center;
        ToolTip.SetTip(clear, "Clear filters");
        AddColumn(title, clear, 1);

        var filters = new StackPanel { Spacing = 4 };
        filters.Children.Add(title);
        var mainRow = Row(
            Field("Description", search),
            Field("Account", account),
            Field("Type", type),
            Field("Category / subcategory", category));
        foreach (var child in mainRow.Children)
        {
            child.Margin = new Thickness(0, 2, 8, 2);
        }
        filters.Children.Add(mainRow);
        var amountRow = Row(
            Field("From", from),
            Field("To", to),
            Field("Min amount", minimum),
            Field("Max amount", maximum));
        foreach (var child in amountRow.Children)
        {
            child.Margin = new Thickness(0, 2, 8, 2);
        }
        filters.Children.Add(amountRow);

        var panel = Panel(filters);
        panel.Padding = new Thickness(10);
        return panel;
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
        var historyList = HistoryList(_overviewHistory?.Hits ?? []);
        _overviewHistoryList = historyList;
        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(0, 0, 0, 10)
        };
        var total = _overviewHistory?.TotalCount ?? 0;
        var first = total == 0 ? 0 : _overviewOffset + 1;
        var last = _overviewOffset + (_overviewHistory?.Hits.Count ?? 0);
        _overviewHistoryHeading = Heading($"Transaction history · {first:N0}-{last:N0} / {total:N0}", 13);
        header.Children.Add(_overviewHistoryHeading);
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
            if (historyList.SelectedItem is HistoryItem item)
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

        var historyContent = new Grid();
        _overviewHistoryEmpty = QuietText("No matching transactions.", 13);
        _overviewHistoryEmpty.IsVisible = total == 0;
        _overviewHistoryEmpty.VerticalAlignment = VerticalAlignment.Top;
        _overviewHistoryEmpty.Margin = new Thickness(0, 6, 0, 0);
        historyContent.Children.Add(_overviewHistoryEmpty);
        historyList.DoubleTapped += async (_, _) =>
        {
            if (historyList.SelectedItem is HistoryItem item)
            {
                await EditTransaction(item.Hit.Entry);
            }
        };
        historyContent.Children.Add(historyList);
        AddRow(body, historyContent, 2);

        var previous = ActionButton("Previous page", async () =>
        {
            _overviewOffset = Math.Max(0, _overviewOffset - HistoryPageSize);
            await RequestOverviewFilterRefresh();
        });
        _overviewPreviousPage = previous;
        previous.IsEnabled = _overviewOffset > 0;
        var next = ActionButton("Next page", async () =>
        {
            _overviewOffset += HistoryPageSize;
            await RequestOverviewFilterRefresh();
        });
        _overviewNextPage = next;
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
