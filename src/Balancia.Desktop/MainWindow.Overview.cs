using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Balancia.Core;
using Balancia.Storage;

namespace Balancia.Desktop;

public partial class MainWindow
{
    private enum OverviewPeriod
    {
        All, ThisWeek, ThisMonth, ThisYear, Custom
    }
    private const int HistoryPageSize = 100;
    private HistoryPage? overviewHistory;
    private int overviewOffset;
    private HistoryFilter overviewFilter = new();
    private bool overviewFiltersVisible;
    private bool pendingOverviewFilterRefresh;
    private OverviewPeriod overviewPeriod = OverviewPeriod.ThisMonth;
    private DateOnly? customFrom;
    private DateOnly? customTo;
    private Grid? overviewLayout;
    private Border? overviewFilterCard;
    private readonly Dictionary<OverviewPeriod, Button> overviewPeriodButtons = [];
    private (OverviewPeriod Period, bool FiltersVisible)? renderedPeriodState;
    private TextBlock? overviewSummaryLabel;
    private TextBlock? overviewStatus;
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

    private Task RefreshFilteredOverview() => RefreshCore(true);

    private async Task RequestOverviewFilterRefresh()
    {
        if (busy)
        {
            pendingOverviewFilterRefresh = true;
            return;
        }

        await Run(RefreshFilteredOverview, false);
    }

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
            overviewFilter.HasCategoryFilter || overviewFilter.Minimum is not null ||
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
            RowDefinitions = new RowDefinitions("Auto,Auto,*"),
            RowSpacing = 11
        };
        var overviewHeader = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto")
        };
        overviewSummaryLabel = QuietText($"{label} · All accounts", 12);
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
        overviewFilterCard = OverviewFilterPanel();
        AddRow(layout, overviewFilterCard, 1);

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
        var accountPanel = Panel(accountRows);
        var (incomeBorder, incomeValue, incomeScope) = Metric("INCOME", ledgerSnapshot.MonthlyIncome, label, "#2C8B6D");
        var (expensesBorder, expensesValue, expensesScope) = Metric("EXPENSES", ledgerSnapshot.MonthlyExpenses, label, "#B95D4D");
        overviewIncomeValue = incomeValue;
        overviewIncomeScope = incomeScope;
        overviewExpensesValue = expensesValue;
        overviewExpensesScope = expensesScope;
        var dashboard = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("0.6*,0.4*,1.5*,1.5*"),
            RowDefinitions = new RowDefinitions("Auto,*"),
            ColumnSpacing = 11
        };
        AddColumn(dashboard, accountPanel, 0);
        var incomeExpenses = new StackPanel
        {
            Spacing = 11,
            Children = { incomeBorder, expensesBorder }
        };
        AddColumn(dashboard, incomeExpenses, 1);
        AddColumn(dashboard, TrendPanel(), 2);
        AddColumn(dashboard, TimelinePanel(), 3);

        var categoryPanel = CategoryManagementPanel(ledgerSnapshot);
        AddRow(dashboard, categoryPanel, 1);
        Grid.SetColumnSpan(categoryPanel, 2);
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
            ColumnDefinitions = new ColumnDefinitions("7*,3*"),
            ColumnSpacing = 11
        };
        AddColumn(historyAndRightPanels, historyPanel, 0);
        AddColumn(historyAndRightPanels, rightColumn, 1);
        AddColumn(dashboard, historyAndRightPanels, 2);
        Grid.SetRow(historyAndRightPanels, 1);
        Grid.SetColumnSpan(historyAndRightPanels, 2);
        AddRow(layout, dashboard, 2);
        overviewLayout = layout;
        ResponsiveBody.Content = layout;
        UpdateAnalyticsCharts();
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
            overviewFilterFrom?.SelectedDate = (overviewFilter.From ?? range.From)?.ToDateTime(TimeOnly.MinValue);

            overviewFilterTo?.SelectedDate = (overviewFilter.To ?? range.To)?.ToDateTime(TimeOnly.MinValue);
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
        Choice<string?>? multipleCategories = null;
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

        type.SelectionChanged += async (_, _) => await ApplyValues();
        account.SelectionChanged += async (_, _) => await ApplyValues();
        category.SelectionChanged += async (_, _) => await ApplyValues();
        syncCategoryFilterChoice = () =>
        {
            if (multipleCategories is not null)
            {
                categories.Remove(multipleCategories);
                multipleCategories = null;
            }

            if (overviewFilter.CategoryIds is { Count: > 1 } selectedIds)
            {
                multipleCategories = new Choice<string?>(null, $"{selectedIds.Count} categories selected");
                categories.Add(multipleCategories);
            }

            category.ItemsSource = categories.ToArray();
            category.SelectedItem = multipleCategories ?? categories.FirstOrDefault(c => c.Value == overviewFilter.CategoryId) ?? categories[0];
            UpdateFilterTone(category, overviewFilter.HasCategoryFilter);
        };
        SyncCategoryFilterChecks();
        account.SelectionChanged += (_, _) => UpdateFilterTone(account, account.SelectedIndex > 0);
        type.SelectionChanged += (_, _) => UpdateFilterTone(type, type.SelectedIndex > 0);
        category.SelectionChanged += (_, _) => UpdateFilterTone(category, category.SelectedIndex > 0);

        from.CalendarClosed += (_, _) => ApplyAfterCalendarClosed();
        to.CalendarClosed += (_, _) => ApplyAfterCalendarClosed();
        from.LostFocus += async (_, _) => await ApplyValues();
        to.LostFocus += async (_, _) => await ApplyValues();
        search.LostFocus += async (_, _) => await ApplyValues();
        minimum.LostFocus += async (_, _) => await ApplyValues();
        maximum.LostFocus += async (_, _) => await ApplyValues();
        search.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await ApplyValues();
            }
        };
        search.TextChanged += (_, _) => UpdateFilterTone(search, !string.IsNullOrWhiteSpace(search.Text));
        minimum.TextChanged += (_, _) => UpdateFilterTone(minimum, !string.IsNullOrWhiteSpace(minimum.Text));
        maximum.TextChanged += (_, _) => UpdateFilterTone(maximum, !string.IsNullOrWhiteSpace(maximum.Text));
        from.SelectedDateChanged += (_, _) => UpdateFilterTone(from, from.SelectedDate is not null);
        to.SelectedDateChanged += (_, _) => UpdateFilterTone(to, to.SelectedDate is not null);

        var filters = new StackPanel { Spacing = 4 };
        var periodRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto")
        };
        var periodButtons = new WrapPanel { Orientation = Orientation.Horizontal };
        periodButtons.Children.Add(QuietText("PERIOD", 11));
        AddColumn(periodRow, periodButtons, 0);
        overviewPeriodButtons.Clear();
        foreach (var (period, periodTitle) in new[] { (OverviewPeriod.All, "All"), (OverviewPeriod.ThisWeek, "Week"),
                     (OverviewPeriod.ThisMonth, "Month"), (OverviewPeriod.ThisYear, "Year") })
        {
            var periodButton = ActionButton(periodTitle, async () =>
            {
                overviewPeriod = period;
                overviewOffset = 0;
                if (period == OverviewPeriod.Custom && (customFrom is null || customTo is null))
                {
                    customFrom = new DateOnly(displayDate.Year, displayDate.Month, 1);
                    customTo = displayDate;
                }

                var range = OverviewRange();
                overviewFilter = overviewFilter with
                {
                    From = range.From,
                    To = range.To
                };
                SyncOverviewFilterDates();
                UpdateOverviewPeriodButtons();
                await RequestOverviewFilterRefresh();
            });
            periodButton.Margin = new Thickness(5, 0, 0, 0);
            periodButtons.Children.Add(periodButton);
            overviewPeriodButtons.Add(period, periodButton);
        }
        filters.Children.Add(periodRow);

        var clear = ActionButton("🗑", async () =>
        {
            overviewFilter = new HistoryFilter();
            overviewOffset = 0;
            updatingFilterControls = true;
            try
            {
                search.Text = string.Empty;
                account.SelectedItem = accountChoices[0];
                type.SelectedItem = types[0];
                category.SelectedItem = categories[0];
                minimum.Text = string.Empty;
                maximum.Text = string.Empty;
                SyncOverviewFilterDates();
            }
            finally
            {
                updatingFilterControls = false;
            }

            SyncCategoryFilterChecks();
            await RequestOverviewFilterRefresh();
        });
        clear.Width = 30;
        clear.Padding = new Thickness(0);
        clear.FontSize = 16;
        clear.HorizontalContentAlignment = HorizontalAlignment.Center;
        ToolTip.SetTip(clear, "Clear filters");
        clear.Margin = new Thickness(8, 0, 0, 0);
        AddColumn(periodRow, clear, 1);
        search.PlaceholderText = "Description";
        account.PlaceholderText = "All accounts ▼";
        type.PlaceholderText = "All types ▼";
        category.PlaceholderText = "All categories ▼";
        minimum.PlaceholderText = "Min amount";
        maximum.PlaceholderText = "Max amount";
        search.Width = 260;
        account.Width = 180;
        type.Width = 155;
        category.Width = 225;
        from.Width = 150;
        to.Width = 150;
        minimum.Width = 125;
        maximum.Width = 125;
        UpdateFilterTone(account, account.SelectedIndex > 0);
        UpdateFilterTone(type, type.SelectedIndex > 0);
        UpdateFilterTone(category, category.SelectedIndex > 0);
        UpdateFilterTone(search, !string.IsNullOrWhiteSpace(search.Text));
        UpdateFilterTone(minimum, !string.IsNullOrWhiteSpace(minimum.Text));
        UpdateFilterTone(maximum, !string.IsNullOrWhiteSpace(maximum.Text));
        UpdateFilterTone(from, from.SelectedDate is not null);
        UpdateFilterTone(to, to.SelectedDate is not null);
        var searchBox = new Grid
        {
            Width = 295
        };
        var clearSearch = new Button
        {
            Content = "×",
            Width = 28,
            Padding = new Thickness(0),
            FontSize = 17,
            Background = Brushes.Transparent,
            Foreground = Brush.Parse("#71838D"),
            HorizontalAlignment = HorizontalAlignment.Right,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        ToolTip.SetTip(clearSearch, "Clear search");
        searchBox.Children.Add(search);
        searchBox.Children.Add(clearSearch);
        clearSearch.Click += async (_, _) =>
        {
            search.Text = string.Empty;
            await ApplyValues();
        };
        var mainRow = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var control in new Control[] { searchBox, account, type, category, from, to, minimum, maximum })
        {
            control.Margin = new Thickness(0, 2, 7, 2);
            mainRow.Children.Add(control);
        }
        filters.Children.Add(mainRow);

        var panel = Panel(filters);
        panel.Padding = new Thickness(10);
        return panel;

        static void UpdateFilterTone(Control control, bool hasValue)
        {
            var foreground = Brush.Parse(hasValue ? "#263C48" : "#71838D");
            switch (control)
            {
                case TextBox textBox:
                    textBox.Foreground = foreground;
                    break;
                case ComboBox comboBox:
                    comboBox.Foreground = foreground;
                    break;
                case CalendarDatePicker datePicker:
                    datePicker.Foreground = foreground;
                    break;
            }
        }

        async Task ApplyValues()
        {
            if (updatingFilterControls || overviewFilterFrom != from)
            {
                return;
            }

            var nextFilter = new HistoryFilter(search.Text, ((Choice<string?>)account.SelectedItem!).Value,
                ((Choice<TransactionKind?>)type.SelectedItem!).Value, ((Choice<string?>)category.SelectedItem!).Value,
                from.SelectedDate is not null ? ParseDate(from) : null,
                to.SelectedDate is not null ? ParseDate(to) : null,
                OptionalMoney(minimum), OptionalMoney(maximum),
                multipleCategories is not null && ReferenceEquals(category.SelectedItem, multipleCategories) ? overviewFilter.CategoryIds : null);
            if (nextFilter == overviewFilter)
            {
                return;
            }

            overviewFilter = nextFilter;
            overviewOffset = 0;
            SyncCategoryFilterChecks();
            await RequestOverviewFilterRefresh();
        }

        void ApplyAfterCalendarClosed()
        {
            // CalendarClosed can precede the control's final SelectedDate
            // property update on the first click. Run after that UI event turn.
            Dispatcher.UIThread.Post(() => _ = ApplyValues());
        }
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
        var border = Panel(new StackPanel
        {
            Spacing = 2,
            Children = { Heading(title, 11), valueText, scopeText }
        });
        return (border, valueText, scopeText);
    }

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
}
