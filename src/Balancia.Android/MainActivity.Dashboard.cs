using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Views;
using Balancia.Core;
using Balancia.Storage;
using Serilog;
using System.Globalization;

namespace Balancia.Android;

public partial class MainActivity
{
    private LinearLayout content = null!;
    private LinearLayout navigation = null!;
    private bool filtersExpanded;
    private bool accountsExpanded;
    private bool categoriesExpanded;
    private bool expendituresExpanded;
    private string period = "Month";
    private DateOnly? customFrom;
    private DateOnly? customTo;
    private Money? minimum;
    private Money? maximum;
    private TransactionKind? kind;
    private readonly HashSet<string> categoryIds = [];
    private string tab = "Overview";
    private DateOnly month = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private string? accountId;
    private FlowInterval interval = FlowInterval.Week;
    private bool expenses = true;
    private int pageOffset;
    private string? descriptionQuery;
    private const int PageSize = 50;
    private CultureInfo DisplayCulture => CultureInfo.GetCultureInfo(
        Resources!.Configuration!.Locales!.Get(0)!.ToLanguageTag()!);

    private int Dp(float value) => (int)(value * Resources!.DisplayMetrics!.Density + .5f);
    private string Amount(Money value) => $"CHF {value.Francs.ToString("N2", DisplayCulture)}";

    private void BuildDashboard()
    {
        ActionBar?.Hide();
        var outer = new SnapshotRefreshLayout(this, RefreshSnapshot) { Orientation = Orientation.Vertical };
        outer.SetFitsSystemWindows(true);
        outer.SetBackgroundColor(ViewerPalette.Background);
        var root = Column();
        outer.AddView(root, new LinearLayout.LayoutParams(-1, -1));
        root.SetPadding(Dp(16), Dp(12), Dp(16), Dp(8));
        root.SetBackgroundColor(ViewerPalette.Background);
        var header = Row();
        header.AddView(Label("Balancia", 24, ViewerPalette.Text, true), new LinearLayout.LayoutParams(0, -2, 1));
        status = Label("No snapshot loaded", 11, ViewerPalette.Muted);
        status.Gravity = GravityFlags.Right | GravityFlags.CenterVertical;
        status.SetSingleLine(true);
        status.Ellipsize = global::Android.Text.TextUtils.TruncateAt.End;
        header.AddView(status, new LinearLayout.LayoutParams(0, -2, 1));
        var themeButton = new ImageButton(this)
        {
            ContentDescription = lightTheme ? "Switch to dark theme" : "Switch to light theme",
            Background = Surface(ViewerPalette.Card),
            ImageTintList = global::Android.Content.Res.ColorStateList.ValueOf(ViewerPalette.Text),
        };
        themeButton.SetImageResource(Resource.Drawable.theme_toggle);
        themeButton.SetPadding(Dp(12), Dp(12), Dp(12), Dp(12));
        themeButton.Click += (_, _) => ToggleTheme();
        header.AddView(themeButton, new LinearLayout.LayoutParams(Dp(48), Dp(48)));
        root.AddView(header);
        var scroll = new ScrollView(this) { FillViewport = true };
        content = Column();
        scroll.AddView(content);
        outer.CanRefresh = () => !importing && !scroll.CanScrollVertically(-1);
        root.AddView(scroll, new LinearLayout.LayoutParams(-1, 0, 1));
        navigation = Row();
        root.AddView(navigation);
        SetContentView(outer);
        RefreshNavigation();
        RefreshDashboard();
    }

    private void PickDocument()
    {
        if (importing) { return; }
        Log.Information("Android snapshot picker opened");
        StartActivityForResult(new Intent(Intent.ActionOpenDocument).SetType("*/*").AddCategory(Intent.CategoryOpenable)
            .AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantPersistableUriPermission), PickSnapshot);
    }

    private void MoveMonth(int direction)
    {
        if (month.Year == 1 && month.Month == 1 && direction < 0 || month.Year == 9999 && month.Month == 12 && direction > 0) { return; }
        month = month.AddMonths(direction);
        period = "Month";
        pageOffset = 0;
        RefreshDashboard();
    }

    private TransactionsFilter Filter()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var from = period switch
        {
            "All" => (DateOnly?)null,
            "Today" => today,
            "Week" => today.AddDays(-((int)today.DayOfWeek + 6) % 7),
            "Year" => new DateOnly(today.Year, 1, 1),
            "Last 30 days" => today.AddDays(-29),
            "Custom" => customFrom,
            _ => month,
        };
        var to = period switch
        {
            "All" => (DateOnly?)null,
            "Today" or "Last 30 days" => today,
            "Week" => from!.Value.AddDays(6),
            "Year" => new DateOnly(today.Year, 12, 31),
            "Custom" => customTo,
            _ => new DateOnly(month.Year, month.Month, DateTime.DaysInMonth(month.Year, month.Month)),
        };
        return new TransactionsFilter(descriptionQuery, accountId, kind, From: from, To: to,
            Minimum: minimum, Maximum: maximum, CategoryIds: categoryIds.ToArray());
    }

    private void ShowFilters()
    {
        var card = Card();
        var presets = Row();
        foreach (var name in new[] { "Month", "All" })
        {
            var button = Action(name, () => { period = name; pageOffset = 0; RefreshDashboard(); });
            button.Background = Surface(period == name ? ViewerPalette.Selected : ViewerPalette.Card);
            presets.AddView(button, new LinearLayout.LayoutParams(0, Dp(36), 1));
        }
        presets.AddView(Action(filtersExpanded ? "⌃" : "⌄", () =>
        {
            filtersExpanded = !filtersExpanded;
            RefreshDashboard();
        }));
        var monthNavigation = Row();
        monthNavigation.AddView(Action("‹", () => MoveMonth(-1)));
        monthNavigation.AddView(Action(month.ToString("MMM yyyy", DisplayCulture), () =>
        {
            period = "Month";
            pageOffset = 0;
            RefreshDashboard();
        }), new LinearLayout.LayoutParams(0, Dp(36), 1));
        monthNavigation.AddView(Action("›", () => MoveMonth(1)));
        card.AddView(presets);
        card.AddView(monthNavigation);
        if (!filtersExpanded) { return; }
        var extra = Row();
        foreach (var name in new[] { "Today", "Week", "Year" })
        {
            extra.AddView(Action(name, () => { period = name; pageOffset = 0; RefreshDashboard(); }), new LinearLayout.LayoutParams(0, Dp(36), 1));
        }
        card.AddView(extra);
        card.AddView(Action("Last 30 days", () => { period = "Last 30 days"; pageOffset = 0; RefreshDashboard(); }));
        card.AddView(Label($"Selected: {period}", 11, ViewerPalette.Muted));
        var dates = Row();
        void AddDate(string title, DateOnly? value, bool start)
        {
            dates.AddView(Action($"{title}: {value?.ToString("dd MMM yyyy", DisplayCulture) ?? "Any"}", () =>
            {
                var date = value ?? DateOnly.FromDateTime(DateTime.Today);
                new DatePickerDialog(this, (_, args) =>
                {
                    var selected = DateOnly.FromDateTime(args.Date);
                    if (start) { customFrom = selected; } else { customTo = selected; }
                    if (customFrom > customTo)
                    {
                        if (start) { customTo = selected; } else { customFrom = selected; }
                    }
                    period = "Custom";
                    pageOffset = 0;
                    RefreshDashboard();
                }, date.Year, date.Month - 1, date.Day).Show();
            }), new LinearLayout.LayoutParams(0, Dp(40), 1));
        }
        AddDate("From", customFrom, true);
        AddDate("To", customTo, false);
        card.AddView(dates);
        var types = new[] { "All types", "Expenses", "Income", "Transfer" };
        card.AddView(Action(kind?.ToString() ?? "All types", () =>
            new AlertDialog.Builder(this).SetTitle("Transaction type")!.SetItems(types, (_, args) =>
            {
                kind = args.Which switch { 1 => TransactionKind.Expense, 2 => TransactionKind.Income, 3 => TransactionKind.Transfer, _ => null };
                pageOffset = 0;
                RefreshDashboard();
            })!.Show()));
        search = Input("Description", descriptionQuery);
        card.AddView(search);
        var amounts = Row();
        var minInput = Input("Min CHF", minimum?.Francs.ToString(CultureInfo.InvariantCulture));
        var maxInput = Input("Max CHF", maximum?.Francs.ToString(CultureInfo.InvariantCulture));
        amounts.AddView(minInput, new LinearLayout.LayoutParams(0, -2, 1));
        amounts.AddView(maxInput, new LinearLayout.LayoutParams(0, -2, 1));
        card.AddView(amounts);
        card.AddView(Action("Apply filters", () =>
        {
            try
            {
                Money? Parse(string? text) => string.IsNullOrWhiteSpace(text) ? null : Money.FromFrancs(decimal.Parse(text.Replace(',', '.'), CultureInfo.InvariantCulture));
                var min = Parse(minInput.Text);
                var max = Parse(maxInput.Text);
                (Filter() with { Minimum = min, Maximum = max }).Validate();
                minimum = min;
                maximum = max;
                descriptionQuery = search.Text;
                pageOffset = 0;
                RefreshDashboard();
            }
            catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException)
            {
                Toast.MakeText(this, "Enter ordered, nonnegative amounts with whole centimes.", ToastLength.Long)!.Show();
            }
        }));
        card.AddView(Action("Clear filters", () =>
        {
            accountId = null;
            categoryIds.Clear();
            kind = null;
            minimum = maximum = null;
            customFrom = customTo = null;
            descriptionQuery = null;
            search = null;
            period = "Month";
            pageOffset = 0;
            RefreshDashboard();
        }));
    }

    private EditText Input(string hint, string? value)
    {
        var input = new EditText(this) { Hint = hint, Text = value, TextSize = 13 };
        input.SetSingleLine(true);
        input.SetTextColor(ViewerPalette.Text);
        input.SetHintTextColor(ViewerPalette.Muted);
        return input;
    }

    private void RefreshNavigation()
    {
        navigation.RemoveAllViews();
        foreach (var name in new[] { "Overview", "Trend", "Timeline", "Reminders" })
        {
            var button = Action(name, () => { tab = name; RefreshNavigation(); RefreshDashboard(); });
            button.Background = Surface(name == tab ? ViewerPalette.Selected : ViewerPalette.Card);
            navigation.AddView(button, new LinearLayout.LayoutParams(0, Dp(58), 1));
        }
    }

    private void RefreshDashboard()
    {
        if (content is null || importing) { return; }
        // Preserve the search value while replacing the overview's controls.
        descriptionQuery = search?.Text ?? descriptionQuery;
        search = null;
        content.RemoveAllViews();
        if (viewerReader is null)
        {
            content.AddView(Label("Your finances, on hand", 24, bold: true));
            content.AddView(Action("Choose snapshot", PickDocument));
            content.AddView(Label("Import a snapshot to see balances, transactions, reminders and charts. Windows remains the only writer.", 16, ViewerPalette.Muted));
            return;
        }
        try
        {
            if (tab != "Reminders") { ShowFilters(); }
            var filter = Filter();
            switch (tab)
            {
                case "Overview": ShowOverview(filter); break;
                case "Reminders": ShowReminders(); break;
                default: ShowChart(filter); break;
            }
        }
        catch (Exception ex)
        {
            Log.Error("Android dashboard read failed, FailureType: '{FailureType}', HResult: '{HResult}'", ex.GetType().Name, ex.HResult);
            content.AddView(Label("This view could not be loaded. Try another period or import a fresh snapshot.", 16, ViewerPalette.Negative));
        }
    }

    private void ShowOverview(TransactionsFilter filter)
    {
        var snapshot = viewerReader!.ReadDashboard(filter);
        var averages = viewerReader.ReadFlowAverages(filter);
        var totals = Card();
        var flowColumns = Row();
        void AddFlow(string title, Money amount, Money average, Color color)
        {
            var column = Column();
            var heading = Row();
            heading.AddView(Label("●", 13, color));
            heading.AddView(Label(title, 13));
            column.AddView(heading);
            column.AddView(Label(Amount(amount), 18, color, true));
            column.AddView(Label($"Average\n{Amount(average)}", 12));
            column.AddView(Label("per calendar month", 10, ViewerPalette.Muted));
            flowColumns.AddView(column, new LinearLayout.LayoutParams(0, -2, 1));
        }
        AddFlow("Income", snapshot.MonthlyIncome, averages.Income, ViewerPalette.Positive);
        var divider = new View(this);
        divider.SetBackgroundColor(ViewerPalette.Border);
        flowColumns.AddView(divider, new LinearLayout.LayoutParams(Dp(1), -1) { LeftMargin = Dp(8), RightMargin = Dp(8) });
        AddFlow("Expenses", snapshot.MonthlyExpenses, averages.Expenses, ViewerPalette.Negative);
        totals.AddView(flowColumns);
        var accounts = Card();
        var selectedAccount = snapshot.Accounts.FirstOrDefault(account => account.Id == accountId);
        var accountHeading = new TableLayout(this);
        accountHeading.SetColumnStretchable(0, true);
        accountHeading.SetColumnShrinkable(0, true);
        var summaryRow = AccountRow($"Account - {selectedAccount?.Name ?? "All"}", selectedAccount?.Balance ?? snapshot.NetWorth,
            accountsExpanded ? "⌃" : "›", false, () =>
            {
                accountsExpanded = !accountsExpanded;
                RefreshDashboard();
            });
        summaryRow.ContentDescription = $"Account - {selectedAccount?.Name ?? "All"}, {Amount(selectedAccount?.Balance ?? snapshot.NetWorth)}, {(accountsExpanded ? "collapse" : "expand")}";
        accountHeading.AddView(summaryRow);
        accounts.AddView(accountHeading);
        if (accountsExpanded)
        {
            var accountTable = new TableLayout(this);
            accountTable.SetColumnStretchable(0, true);
            accountTable.SetColumnShrinkable(0, true);
            void AddAccount(string? id, string name, Money balance)
            {
                var selected = accountId == id;
                accountTable.AddView(AccountRow(name, balance, selected ? "✓" : string.Empty, selected, () =>
                {
                    accountId = id;
                    pageOffset = 0;
                    RefreshDashboard();
                }));
            }
            AddAccount(null, "All", snapshot.NetWorth);
            foreach (var account in snapshot.Accounts)
            {
                AddAccount(account.Id, account.Name, account.Balance);
            }
            accounts.AddView(accountTable);
        }
        var categories = Card();
        categories.AddView(Action($"Categories · {(categoryIds.Count == 0 ? "All" : $"{categoryIds.Count} selected")}  {(categoriesExpanded ? "⌃" : "›")}", () => { categoriesExpanded = !categoriesExpanded; RefreshDashboard(); }));
        if (categoriesExpanded)
        {
            void AddCategory(string title, bool selected, System.Action<bool> changed)
            {
                var check = new CheckBox(this) { Text = title, Checked = selected, TextSize = 13 };
                check.SetTextColor(ViewerPalette.Text);
                check.ButtonTintList = global::Android.Content.Res.ColorStateList.ValueOf(ViewerPalette.Text);
                check.CheckedChange += (_, args) => { changed(args.IsChecked); pageOffset = 0; RefreshDashboard(); };
                categories.AddView(check);
            }
            AddCategory("All", categoryIds.Count == 0, _ => categoryIds.Clear());
            foreach (var category in snapshot.Categories.Where(category => category.ParentId is null))
            {
                var children = snapshot.Categories.Where(child => child.ParentId == category.Id).ToArray();
                var ids = children.Select(child => child.Id).Prepend(category.Id).ToArray();
                AddCategory(category.Name, ids.All(categoryIds.Contains), selected =>
                {
                    foreach (var id in ids) { if (selected) { categoryIds.Add(id); } else { categoryIds.Remove(id); } }
                });
                foreach (var child in children)
                {
                    AddCategory("    " + child.Name, categoryIds.Contains(child.Id), selected =>
                    {
                        // Expand a selected parent into explicit children before changing one child.
                        if (categoryIds.Remove(category.Id))
                        {
                            foreach (var sibling in children) { categoryIds.Add(sibling.Id); }
                        }
                        if (selected) { categoryIds.Add(child.Id); } else { categoryIds.Remove(child.Id); }
                    });
                }
            }
        }
        var expenditures = Card();
        expenditures.AddView(Action("Top expenditures" + (expendituresExpanded ? "  ⌃" : "  ›"), () => { expendituresExpanded = !expendituresExpanded; RefreshDashboard(); }));
        foreach (var category in snapshot.LargestCategories.Take(expendituresExpanded ? int.MaxValue : 1))
        {
            var row = Row();
            row.AddView(Label(category.Name, 13), new LinearLayout.LayoutParams(0, -2, 1));
            row.AddView(Label(Amount(category.Amount), 13));
            expenditures.AddView(row);
            var bar = new ProgressBar(this, null, global::Android.Resource.Attribute.ProgressBarStyleHorizontal)
            {
                Max = 1000,
                Progress = snapshot.MonthlyExpenses.Centimes == 0 ? 0 : (int)(category.Amount.Centimes * 1000m / snapshot.MonthlyExpenses.Centimes),
                ProgressTintList = global::Android.Content.Res.ColorStateList.ValueOf(ViewerPalette.Negative),
            };
            expenditures.AddView(bar);
        }
        var transactions = Card();
        var page = viewerReader.ReadTransactions(filter, pageOffset, PageSize);
        transactions.AddView(Label($"TRANSACTIONS ({page.TotalCount})", 12, ViewerPalette.Muted));
        foreach (var hit in page.Hits)
        {
            var entry = hit.Entry;
            var color = entry.Draft.Kind == TransactionKind.Income ? ViewerPalette.Positive : entry.Draft.Kind == TransactionKind.Expense ? ViewerPalette.Negative : ViewerPalette.Accent;
            var row = Row();
            row.SetPadding(Dp(2), Dp(3), Dp(2), Dp(3));
            row.Background = Surface(entry.Draft.Date.DayNumber % 2 == 0 ? ViewerPalette.Card : ViewerPalette.Background);
            var date = Label(entry.Draft.Date.ToString("dd\nMMM", DisplayCulture), 12, bold: true);
            date.Gravity = GravityFlags.Center;
            row.AddView(date, new LinearLayout.LayoutParams(Dp(42), -2));
            var description = Column();
            description.AddView(Label(entry.Draft.Description, 13, bold: true));
            description.AddView(Label($"{entry.CategoryPath ?? entry.Draft.Kind.ToString()} · {entry.AccountName}" + (entry.DestinationName is null ? string.Empty : $" → {entry.DestinationName}"), 10, ViewerPalette.Muted));
            row.AddView(description, new LinearLayout.LayoutParams(0, -2, 1));
            row.AddView(Label($"{(entry.Draft.Kind == TransactionKind.Expense ? "−" : entry.Draft.Kind == TransactionKind.Income ? "+" : "↔")} {Amount(entry.Draft.Amount)}", 12, color, true));
            row.Click += (_, _) => new AlertDialog.Builder(this).SetTitle(entry.Draft.Description)!.SetMessage($"{entry.Draft.Date.ToString("dd MMM yyyy", DisplayCulture)}\n{entry.AccountName}\n{entry.CategoryPath}\n{entry.Draft.Memo}")!.SetPositiveButton("Close", (_, _) => { })!.Show();
            transactions.AddView(row);
        }
        if (page.Hits.Count == 0) { transactions.AddView(Label("No matching transactions.", 15, ViewerPalette.Muted)); }
        transactions.AddView(Label($"Showing {page.Hits.Count} of {page.TotalCount} · Read only", 12, ViewerPalette.Muted));
        var paging = Row();
        if (pageOffset > 0) { paging.AddView(Action("Previous", () => { pageOffset = Math.Max(0, pageOffset - PageSize); RefreshDashboard(); })); }
        if (pageOffset + page.Hits.Count < page.TotalCount) { paging.AddView(Action("Next 50", () => { pageOffset += PageSize; RefreshDashboard(); })); }
        transactions.AddView(paging);
        Log.Information("Android transaction search completed, ResultCount: '{ResultCount}'", page.Hits.Count);
    }

    private TableRow AccountRow(string name, Money balance, string marker, bool selected, System.Action click)
    {
        var row = new TableRow(this)
        {
            Clickable = true,
            Focusable = true,
            ContentDescription = $"{name}, {Amount(balance)}{(selected ? ", selected" : string.Empty)}",
            Background = Surface(selected ? ViewerPalette.Selected : ViewerPalette.Card),
        };
        row.SetMinimumHeight(Dp(44));
        row.SetGravity(GravityFlags.CenterVertical);
        // TableLayout allocates the full row width to columns; horizontal row padding would push the last column outside it.
        row.SetPadding(0, Dp(4), 0, Dp(4));
        var title = Label(name, 13);
        title.SetPadding(Dp(9), Dp(5), Dp(3), Dp(5));
        title.SetSingleLine(true);
        title.Ellipsize = global::Android.Text.TextUtils.TruncateAt.End;
        row.AddView(title);
        var amount = Label(Amount(balance), 13, bold: true);
        amount.SetSingleLine(true);
        amount.Gravity = GravityFlags.Right | GravityFlags.CenterVertical;
        amount.SetPadding(Dp(8), Dp(5), Dp(3), Dp(5));
        row.AddView(amount);
        var indicator = Label(marker, 14);
        indicator.Gravity = GravityFlags.Center;
        indicator.SetSingleLine(true);
        indicator.SetMinWidth(Dp(32));
        indicator.SetPadding(Dp(4), Dp(5), Dp(8), Dp(5));
        row.AddView(indicator, new TableRow.LayoutParams(Dp(32), -2));
        row.Click += (_, _) => click();
        return row;
    }

    private void ShowReminders()
    {
        var reminders = viewerReader!.ReadReminders();
        var today = DateOnly.FromDateTime(DateTime.Today);
        var total = reminders.Aggregate(Money.Zero, (sum, reminder) => sum + reminder.Template.IndicativeAmount);
        var summary = Card();
        var hasPendingThisMonth = reminders.Any(reminder => reminder.Occurrence.Year == today.Year && reminder.Occurrence.Month == today.Month);
        var upcomingMonth = hasPendingThisMonth ? today : today.AddMonths(1);
        var upcoming = reminders.Where(reminder => reminder.Occurrence.Year == upcomingMonth.Year && reminder.Occurrence.Month == upcomingMonth.Month)
            .Aggregate(Money.Zero, (sum, reminder) => sum + reminder.Template.IndicativeAmount);
        summary.AddView(Label("Total", 13, ViewerPalette.Muted));
        summary.AddView(Label(Amount(total), 24, bold: true));
        summary.AddView(Label(hasPendingThisMonth ? "Upcoming this month" : "Upcoming next month", 13, ViewerPalette.Muted));
        summary.AddView(Label(Amount(upcoming), 21, ViewerPalette.Accent, true));
        var list = Card();
        foreach (var reminder in reminders.OrderBy(reminder => reminder.Occurrence))
        {
            var days = reminder.Occurrence.DayNumber - today.DayNumber;
            var color = reminder.Overdue ? ViewerPalette.Negative : days is >= 0 and <= 5 ? ViewerPalette.Warning
                : ReminderSchedule.MonthsUntil(reminder.Occurrence, today) switch
                {
                    1 => ViewerPalette.Accent,
                    > 1 => ViewerPalette.Positive,
                    _ => ViewerPalette.Text,
                };
            var row = Row();
            row.Background = Surface(ReminderSchedule.MonthsUntil(reminder.Occurrence, today) >= 2 ? ViewerPalette.Background : ViewerPalette.Card);
            row.AddView(Label(reminder.Occurrence.ToString("dd\nMMM", DisplayCulture), 12, color, true), new LinearLayout.LayoutParams(Dp(45), -2));
            var description = Column();
            description.AddView(Label(reminder.Template.Description, 13, color, true));
            description.AddView(Label(days < 0 ? $"{-(long)days} days overdue" : days == 0 ? "Due today" : $"in {days} days", 11, color));
            row.AddView(description, new LinearLayout.LayoutParams(0, -2, 1));
            row.AddView(Label(Amount(reminder.Template.IndicativeAmount), 13, color, true));
            list.AddView(row);
        }
        if (reminders.Count == 0) { content.AddView(Label("No active reminders in this snapshot.", 16, ViewerPalette.Muted)); }
    }

    private void ShowChart(TransactionsFilter filter)
    {
        var controls = Row();
        if (tab == "Trend")
        {
            foreach (var value in Enum.GetValues<FlowInterval>())
            {
                controls.AddView(Action(value == interval ? $"● {value}" : value.ToString(), () => { interval = value; RefreshDashboard(); }));
            }
        }
        else
        {
            var income = Action("Income", () => { expenses = false; RefreshDashboard(); });
            var expense = Action("Expenses", () => { expenses = true; RefreshDashboard(); });
            income.Background = Surface(expenses ? ViewerPalette.Card : ViewerPalette.Selected);
            expense.Background = Surface(expenses ? ViewerPalette.Selected : ViewerPalette.Card);
            controls.AddView(income, new LinearLayout.LayoutParams(0, Dp(38), 1));
            controls.AddView(expense, new LinearLayout.LayoutParams(0, Dp(38), 1));
        }
        content.AddView(controls);
        var analytics = viewerReader!.ReadFlowAnalytics(filter);
        var card = Card();
        var detail = Column();
        var heading = Label("Tap the chart to inspect a date or interval", 14, bold: true);
        detail.AddView(heading);
        var table = new TableLayout(this);
        table.SetColumnStretchable(1, true);
        table.SetColumnShrinkable(0, true);
        var values = new List<TextView>();
        var labels = tab == "Timeline" ? new[] { "Current", "Previous", "Difference" } : new[] { "Income", "Expenses", "Savings" };
        foreach (var name in labels)
        {
            var row = new TableRow(this);
            row.AddView(Label(name, 14));
            var value = Label("—", 14, bold: true);
            value.Gravity = GravityFlags.Right | GravityFlags.CenterVertical;
            value.SetSingleLine(true);
            row.AddView(value);
            table.AddView(row);
            values.Add(value);
        }
        detail.AddView(table);
        var timeline = tab == "Timeline";
        void ShowDetail(string title, Money first, Money second, Money third)
        {
            heading.Text = title;
            values[0].Text = Amount(first);
            values[1].Text = Amount(second);
            values[2].Text = Amount(third);
            values[0].SetTextColor(timeline ? (expenses ? ViewerPalette.Negative : ViewerPalette.Positive) : ViewerPalette.Positive);
            values[1].SetTextColor(timeline ? ViewerPalette.Accent : ViewerPalette.Negative);
            values[2].SetTextColor(third < Money.Zero ? ViewerPalette.Negative : ViewerPalette.Accent);
        }
        var chart = new ViewerChart(this, analytics, interval, timeline, expenses, DisplayCulture, ShowDetail);
        card.AddView(chart, new LinearLayout.LayoutParams(-1, Dp(timeline ? 260 : 340)));
        if (timeline)
        {
            void AddLegend(string stroke, string title, string dates, Color color)
            {
                var legend = Row();
                legend.AddView(Label(stroke, 18, color, true));
                legend.AddView(Label(title, 12, color), new LinearLayout.LayoutParams(0, -2, 1));
                legend.AddView(Label(dates, 11, ViewerPalette.Muted));
                card.AddView(legend);
            }
            AddLegend("━", "Current", $"{analytics.From.ToString("dd MMM", DisplayCulture)} – {analytics.To.ToString("dd MMM", DisplayCulture)}", expenses ? ViewerPalette.Negative : ViewerPalette.Positive);
            if (analytics.PreviousFrom is { } from && analytics.PreviousTo is { } to)
            {
                AddLegend("┄", "Previous", $"{from.ToString("dd MMM", DisplayCulture)} – {to.ToString("dd MMM", DisplayCulture)}", ViewerPalette.Accent);
            }
            var summary = Card();
            summary.AddView(detail);
        }
        else
        {
            card.AddView(detail);
        }
        if (analytics.Current.Count == 0) { card.AddView(Label("No income or expenses in this period.", 15, ViewerPalette.Muted)); }
    }

    private LinearLayout Column() => new(this) { Orientation = Orientation.Vertical };
    private LinearLayout Row()
    {
        var row = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        row.SetGravity(GravityFlags.CenterVertical);
        return row;
    }
    private LinearLayout Card()
    {
        var card = Column();
        card.SetPadding(Dp(12), Dp(10), Dp(12), Dp(10));
        card.Background = Surface(ViewerPalette.Card);
        card.LayoutParameters = new LinearLayout.LayoutParams(-1, -2) { TopMargin = Dp(10), BottomMargin = Dp(4) };
        content.AddView(card);
        return card;
    }

    private TextView Label(string text, float size, Color? color = null, bool bold = false)
    {
        var label = new TextView(this) { Text = text, TextSize = size };
        label.SetTextColor(color ?? ViewerPalette.Text);
        label.SetPadding(Dp(3), Dp(5), Dp(3), Dp(5));
        if (bold) { label.SetTypeface(null, TypefaceStyle.Bold); }
        return label;
    }

    private Button Action(string title, System.Action click)
    {
        var button = new Button(this) { Text = title, TextSize = 12 };
        button.SetAllCaps(false);
        button.SetTextColor(ViewerPalette.Text);
        button.SetPadding(Dp(8), Dp(4), Dp(8), Dp(4));
        button.SetMinimumHeight(Dp(36));
        button.SetMinHeight(Dp(36));
        button.SetMinimumWidth(Dp(32));
        button.SetMinWidth(Dp(32));
        button.Background = Surface(ViewerPalette.Card);
        button.Click += (_, _) => click();
        return button;
    }

    private GradientDrawable Surface(Color color)
    {
        var background = new GradientDrawable();
        background.SetColor(color);
        background.SetCornerRadius(Dp(10));
        background.SetStroke(Dp(1), ViewerPalette.Border);
        return background;
    }
}
