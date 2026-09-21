using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Balancia.Core;
using Balancia.Storage;

namespace Balancia.Desktop;

public partial class MainWindow
{
    private Grid? overviewLayout;
    private StackPanel? overviewFilterBox;
    private Border? overviewFilterCard;
    private readonly Dictionary<OverviewPeriod, Button> overviewPeriodButtons = new();
    private (OverviewPeriod Period, bool FiltersVisible)? renderedPeriodState;
    private TextBlock? overviewSummaryLabel;
    private TextBlock? overviewIncomeValue;
    private TextBlock? overviewIncomeScope;
    private TextBlock? overviewExpensesValue;
    private TextBlock? overviewExpensesScope;
    private StackPanel? overviewCategoryBody;
    private TextBlock? overviewCategoryHeading;
    private IReadOnlyList<CategoryTotal> renderedOverviewCategories = [];
    private string? renderedOverviewCategoryId;
    private TextBlock? overviewHistoryHeading;
    private ListBox? overviewHistoryList;
    private TextBlock? overviewHistoryEmpty;
    private Button? overviewPreviousPage;
    private Button? overviewNextPage;
    private CalendarDatePicker? overviewFilterFrom;
    private CalendarDatePicker? overviewFilterTo;
    private bool updatingFilterControls;

    private (DateOnly? From, DateOnly? To) OverviewRange() => overviewPeriod switch
    {
        OverviewPeriod.All => (null, null),
        OverviewPeriod.ThisWeek => WeekRange(displayDate),
        OverviewPeriod.ThisMonth => (new DateOnly(displayDate.Year, displayDate.Month, 1),
            new DateOnly(displayDate.Year, displayDate.Month, 1).AddMonths(1).AddDays(-1)),
        OverviewPeriod.ThisYear => (new DateOnly(displayDate.Year, 1, 1), new DateOnly(displayDate.Year, 12, 31)),
        _ => (_customFrom: customFrom, _customTo: customTo)
    };

    private static (DateOnly From, DateOnly To) WeekRange(DateOnly date)
    {
        var daysSinceMonday = ((int)date.DayOfWeek + 6) % 7;
        var monday = date.AddDays(-daysSinceMonday);
        return (monday, monday.AddDays(6));
    }

    private string OverviewPeriodLabel() => overviewPeriod switch
    {
        OverviewPeriod.All => "All dates",
        OverviewPeriod.ThisWeek => "This week",
        OverviewPeriod.ThisMonth => displayDate.ToString("MMMM yyyy", CultureInfo.CurrentCulture),
        OverviewPeriod.ThisYear => displayDate.Year.ToString(CultureInfo.CurrentCulture),
        _ => $"{customFrom:dd MMM yyyy} – {customTo:dd MMM yyyy}"
    };

    private string OverviewScopeLabel()
    {
        var range = OverviewRange();
        var hasAdditionalFilter = !string.IsNullOrEmpty(overviewFilter.Description) ||
            overviewFilter.AccountId is not null || overviewFilter.Kind is not null ||
            overviewFilter.CategoryId is not null || overviewFilter.Minimum is not null ||
            overviewFilter.Maximum is not null ||
            (overviewFilter.From ?? range.From) != range.From ||
            (overviewFilter.To ?? range.To) != range.To;
        return hasAdditionalFilter ? "Filtered transactions" : OverviewPeriodLabel();
    }

    private void RenderOverview(LedgerSnapshot ledgerSnapshot)
    {
        var label = OverviewScopeLabel();
        overviewFilterCard = null;
        overviewFilterFrom = null;
        overviewFilterTo = null;
        renderedPeriodState = null;
        var layout = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*"),
            RowSpacing = 11
        };
        overviewSummaryLabel = QuietText($"{label} · All accounts", 12);
        AddRow(layout, overviewSummaryLabel, 0);
        var filterBox = new StackPanel { Spacing = 8 };
        overviewFilterBox = filterBox;
        overviewPeriodButtons.Clear();
        var filters = new WrapPanel();
        filters.Children.Add(QuietText("PERIOD", 11));
        foreach (var (period, title) in new[] { (OverviewPeriod.All, "All"), (OverviewPeriod.ThisWeek, "This Week"),
                     (OverviewPeriod.ThisMonth, "This Month"), (OverviewPeriod.ThisYear, "This Year"), (OverviewPeriod.Custom, "Filter") })
        {
            var button = ActionButton(title, async () =>
            {
                if (period == OverviewPeriod.Custom)
                {
                    overviewFiltersVisible = !overviewFiltersVisible;
                    UpdateOverviewFilterVisibility();
                    return;
                }

                overviewPeriod = period;
                overviewOffset = 0;
                if (period == OverviewPeriod.Custom && (customFrom is null || customTo is null))
                {
                    customFrom = new DateOnly(displayDate.Year, displayDate.Month, 1);
                    customTo = displayDate;
                }

                // Keep the advanced filter fields in sync with the selected period
                // shortcut so the active date range is visible when Filter opens.
                var range = OverviewRange();
                overviewFilter = overviewFilter with { From = range.From, To = range.To };

                SyncOverviewFilterDates();
                await RequestOverviewFilterRefresh();
            });
            button.Margin = new Thickness(5, 0, 0, 0);
            if (overviewPeriod == period || period == OverviewPeriod.Custom && overviewFiltersVisible)
            {
                button.Background = Brush.Parse("#D8ECF3");
                button.Foreground = Brush.Parse("#1C627E");
                button.FontWeight = FontWeight.SemiBold;
            }
            filters.Children.Add(button);
            overviewPeriodButtons.Add(period, button);
        }
        filterBox.Children.Add(filters);
        UpdateOverviewFilterVisibility();
        if (overviewPeriod == OverviewPeriod.Custom)
        {
            var from = DateInput(customFrom);
            var to = DateInput(customTo);
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
                customFrom = first;
                customTo = last;
                overviewFilter = overviewFilter with { From = first, To = last };
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
            ItemsSource = ledgerSnapshot.Accounts.Select(a => new Choice<Account>(a, a.Name)).ToArray(),
            MinHeight = ledgerSnapshot.Accounts.Count == 0 ? 0 : 45,
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

        if (ledgerSnapshot.Accounts.Count == 0)
        {
            accountRows.Children.Add(QuietText("No accounts yet", 12));
        }
        else
        {
            accountRows.Children.Add(accounts);
        }

        var total = TwoColumn("Total net worth", AmountText(ledgerSnapshot.NetWorth), 16,
            BalanceColor(ledgerSnapshot.NetWorth));
        total.Margin = new Thickness(0, 9, 0, 0);
        accountRows.Children.Add(total);
        AddColumn(summary, Panel(accountRows), 0);
        var income = Metric("INCOME", ledgerSnapshot.MonthlyIncome, label, "#2C8B6D");
        var expenses = Metric("EXPENSES", ledgerSnapshot.MonthlyExpenses, label, "#B95D4D");
        var incomeBody = (StackPanel)income.Child!;
        var expensesBody = (StackPanel)expenses.Child!;
        overviewIncomeValue = (TextBlock)incomeBody.Children[1];
        overviewIncomeScope = (TextBlock)incomeBody.Children[2];
        overviewExpensesValue = (TextBlock)expensesBody.Children[1];
        overviewExpensesScope = (TextBlock)expensesBody.Children[2];
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
        right.Children.Add(CategoriesPanel(ledgerSnapshot));
        right.Children.Add(RemindersPanel());
        AddColumn(lower, right, 1);
        AddRow(layout, lower, 3);
        overviewLayout = layout;
        ResponsiveBody.Content = layout;
    }

    private void UpdateOverviewFilterVisibility()
    {
        if (overviewFilterBox is null)
        {
            return;
        }

        if (overviewFiltersVisible)
        {
            overviewFilterCard ??= OverviewFilterPanel();
            if (!overviewFilterBox.Children.Contains(overviewFilterCard))
            {
                overviewFilterBox.Children.Add(overviewFilterCard);
            }
        }
        else if (overviewFilterCard is not null)
        {
            updatingFilterControls = true;
            try
            {
                overviewFilterBox.Children.Remove(overviewFilterCard);
            }
            finally
            {
                updatingFilterControls = false;
            }
        }

        UpdateOverviewPeriodButtons();
    }

    private void UpdateOverviewPeriodButtons()
    {
        var current = (_overviewPeriod: overviewPeriod, _overviewFiltersVisible: overviewFiltersVisible);
        if (renderedPeriodState == current)
        {
            return;
        }

        foreach (var (period, button) in overviewPeriodButtons)
        {
            var selected = period == overviewPeriod ||
                period == OverviewPeriod.Custom && overviewFiltersVisible;
            if (selected)
            {
                button.Background = Brush.Parse("#D8ECF3");
                button.Foreground = Brush.Parse("#1C627E");
                button.FontWeight = FontWeight.SemiBold;
            }
            else
            {
                button.ClearValue(BackgroundProperty);
                button.ClearValue(ForegroundProperty);
                button.ClearValue(FontWeightProperty);
            }
        }

        renderedPeriodState = current;
    }

    private void SyncOverviewFilterDates()
    {
        var range = OverviewRange();
        updatingFilterControls = true;
        try
        {
            if (overviewFilterFrom is not null)
            {
                overviewFilterFrom.SelectedDate = (overviewFilter.From ?? range.From)?.ToDateTime(TimeOnly.MinValue);
            }

            if (overviewFilterTo is not null)
            {
                overviewFilterTo.SelectedDate = (overviewFilter.To ?? range.To)?.ToDateTime(TimeOnly.MinValue);
            }
        }
        finally
        {
            updatingFilterControls = false;
        }
    }

    private void UpdateOverviewInPlace()
    {
        if (snapshot is not { } ledgerSnapshot)
        {
            return;
        }

        var label = OverviewScopeLabel();
        overviewSummaryLabel!.Text = $"{label} · All accounts";
        overviewIncomeValue!.Text = AmountText(ledgerSnapshot.MonthlyIncome);
        overviewIncomeScope!.Text = label;
        overviewExpensesValue!.Text = AmountText(ledgerSnapshot.MonthlyExpenses);
        overviewExpensesScope!.Text = label;
        UpdateOverviewPeriodButtons();
        if (renderedOverviewCategoryId != overviewFilter.CategoryId ||
            !renderedOverviewCategories.SequenceEqual(ledgerSnapshot.LargestCategories))
        {
            FillCategoriesPanel(overviewCategoryBody!, ledgerSnapshot);
        }

        var total = overviewHistory?.TotalCount ?? 0;
        var first = total == 0 ? 0 : overviewOffset + 1;
        var last = overviewOffset + (overviewHistory?.Hits.Count ?? 0);
        overviewHistoryHeading!.Text = $"Transaction history · {first:N0}-{last:N0} / {total:N0}";
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

    private Border OverviewFilterPanel()
    {
        var activeFilter = overviewFilter with
        {
            From = overviewFilter.From ?? OverviewRange().From,
            To = overviewFilter.To ?? OverviewRange().To
        };
        var search = Input(activeFilter.Description ?? "");
        search.Width = 300;
        search.HorizontalAlignment = HorizontalAlignment.Left;
        var accountChoices = new List<Choice<string?>> { new(null, "All accounts") };
        accountChoices.AddRange((snapshot?.Accounts ?? []).Select(a => new Choice<string?>(a.Id, a.Name)));
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
        categories.AddRange((snapshot?.Categories ?? []).Select(c => new Choice<string?>(c.Id, c.Path)));
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
        overviewFilterFrom = from;
        overviewFilterTo = to;
        var minimum = Input(activeFilter.Minimum?.Francs.ToString("0.00", CultureInfo.InvariantCulture) ?? "");
        minimum.Width = 130;
        var maximum = Input(activeFilter.Maximum?.Francs.ToString("0.00", CultureInfo.InvariantCulture) ?? "");
        maximum.Width = 130;
        async Task ApplyValues()
        {
            if (updatingFilterControls || overviewFilterFrom != from)
            {
                return;
            }

            var nextFilter = new HistoryFilter(search.Text, ((Choice<string?>)account.SelectedItem!).Value,
                ((Choice<TransactionKind?>)type.SelectedItem!).Value, ((Choice<string?>)category.SelectedItem!).Value,
                from.SelectedDate is { } ? ParseDate(from) : null,
                to.SelectedDate is { } ? ParseDate(to) : null,
                OptionalMoney(minimum), OptionalMoney(maximum));
            if (nextFilter == overviewFilter)
            {
                return;
            }

            overviewFilter = nextFilter;
            overviewOffset = 0;
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
            overviewFilter = new HistoryFilter();
            overviewOffset = 0;
            updatingFilterControls = true;
            try
            {
                if (overviewFilterCard is not null)
                {
                    overviewFilterBox?.Children.Remove(overviewFilterCard);
                    overviewFilterCard = null;
                }

                overviewFilterFrom = null;
                overviewFilterTo = null;
                UpdateOverviewFilterVisibility();
            }
            finally
            {
                updatingFilterControls = false;
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
        var historyList = HistoryList(overviewHistory?.Hits ?? []);
        overviewHistoryList = historyList;
        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(0, 0, 0, 10)
        };
        var total = overviewHistory?.TotalCount ?? 0;
        var first = total == 0 ? 0 : overviewOffset + 1;
        var last = overviewOffset + (overviewHistory?.Hits.Count ?? 0);
        overviewHistoryHeading = Heading($"Transaction history · {first:N0}-{last:N0} / {total:N0}", 13);
        header.Children.Add(overviewHistoryHeading);
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
        overviewHistoryEmpty = QuietText("No matching transactions.", 13);
        overviewHistoryEmpty.IsVisible = total == 0;
        overviewHistoryEmpty.VerticalAlignment = VerticalAlignment.Top;
        overviewHistoryEmpty.Margin = new Thickness(0, 6, 0, 0);
        historyContent.Children.Add(overviewHistoryEmpty);
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
            overviewOffset = Math.Max(0, overviewOffset - HistoryPageSize);
            await RequestOverviewFilterRefresh();
        });
        overviewPreviousPage = previous;
        previous.IsEnabled = overviewOffset > 0;
        var next = ActionButton("Next page", async () =>
        {
            overviewOffset += HistoryPageSize;
            await RequestOverviewFilterRefresh();
        });
        overviewNextPage = next;
        next.IsEnabled = overviewHistory is { } currentPage && overviewOffset + currentPage.Hits.Count < currentPage.TotalCount;
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
