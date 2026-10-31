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
    private TextView periodLabel = null!;
    private string tab = "Overview";
    private DateOnly month = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private bool rolling;
    private string? accountId;
    private string? categoryId;
    private FlowInterval interval = FlowInterval.Week;
    private bool expenses = true;
    private int pageOffset;
    private string? descriptionQuery;
    private const int PageSize = 50;
    private static readonly CultureInfo DisplayCulture = CultureInfo.GetCultureInfo("de-CH");

    private int Dp(float value) => (int)(value * Resources!.DisplayMetrics!.Density + .5f);
    private static string Amount(Money value) => $"CHF {value.Francs.ToString("N2", DisplayCulture)}";

    private void BuildDashboard()
    {
        ActionBar?.Hide();
        var outer = Column();
        outer.SetFitsSystemWindows(true);
        outer.SetBackgroundColor(ViewerPalette.Background);
        var root = Column();
        outer.AddView(root, new LinearLayout.LayoutParams(-1, -1));
        root.SetPadding(Dp(16), Dp(12), Dp(16), Dp(8));
        root.SetBackgroundColor(ViewerPalette.Background);
        var header = Row();
        header.AddView(Label("Balancia", 28, ViewerPalette.Text, true), new LinearLayout.LayoutParams(0, -2, 1));
        header.AddView(Action("↻ Import", PickDocument));
        root.AddView(header);
        status = Label("No snapshot loaded. Import an encrypted Windows snapshot.", 12, ViewerPalette.Muted);
        root.AddView(status);
        root.AddView(Label("PERIOD", 11, ViewerPalette.Muted));
        var periods = Row();
        periods.AddView(Action("Month", () => { rolling = false; pageOffset = 0; RefreshDashboard(); }));
        periods.AddView(Action("Last 30 days", () => { rolling = true; pageOffset = 0; RefreshDashboard(); }));
        periods.AddView(Action("‹", () => MoveMonth(-1)));
        periodLabel = Label(string.Empty, 13);
        periods.AddView(periodLabel, new LinearLayout.LayoutParams(0, -2, 1));
        periods.AddView(Action("›", () => MoveMonth(1)));
        root.AddView(periods);
        var scroll = new ScrollView(this) { FillViewport = true };
        content = Column();
        scroll.AddView(content);
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
        StartActivityForResult(new Intent(Intent.ActionOpenDocument).SetType("*/*").AddCategory(Intent.CategoryOpenable), PickSnapshot);
    }

    private void MoveMonth(int direction)
    {
        if (month.Year == 1 && month.Month == 1 && direction < 0 || month.Year == 9999 && month.Month == 12 && direction > 0) { return; }
        month = month.AddMonths(direction);
        rolling = false;
        pageOffset = 0;
        RefreshDashboard();
    }

    private TransactionsFilter Filter()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        return new TransactionsFilter(search?.Text, accountId, CategoryId: categoryId,
            From: rolling ? today.AddDays(-29) : month,
            To: rolling ? today : new DateOnly(month.Year, month.Month, DateTime.DaysInMonth(month.Year, month.Month)));
    }

    private void RefreshNavigation()
    {
        navigation.RemoveAllViews();
        foreach (var name in new[] { "Overview", "Reminders", "Trend", "Timeline" })
        {
            var button = Action(name, () => { tab = name; RefreshNavigation(); RefreshDashboard(); });
            button.Background = Surface(name == tab ? ViewerPalette.Selected : ViewerPalette.Card);
            navigation.AddView(button, new LinearLayout.LayoutParams(0, Dp(58), 1));
        }
    }

    private void RefreshDashboard()
    {
        if (content is null || importing) { return; }
        periodLabel.Text = rolling ? "Last 30 days" : month.ToString("MMM yyyy", DisplayCulture);
        // Preserve the search value while replacing the overview's controls.
        descriptionQuery = search?.Text ?? descriptionQuery;
        search = null;
        content.RemoveAllViews();
        if (viewerReader is null)
        {
            content.AddView(Label("Your finances, on hand", 24, bold: true));
            content.AddView(Label("Import a snapshot to see balances, transactions, reminders and charts. Windows remains the only writer.", 16, ViewerPalette.Muted));
            return;
        }
        try
        {
            var filter = Filter() with { Description = descriptionQuery };
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
            column.AddView(Label(title, 15, color));
            column.AddView(Label(Amount(amount), 21, color, true));
            column.AddView(Label($"Average\n{Amount(average)}\nper calendar month", 12, ViewerPalette.Muted));
            flowColumns.AddView(column, new LinearLayout.LayoutParams(0, -2, 1));
        }
        AddFlow("● Income", snapshot.MonthlyIncome, averages.Income, ViewerPalette.Positive);
        AddFlow("● Expenses", snapshot.MonthlyExpenses, averages.Expenses, ViewerPalette.Negative);
        totals.AddView(flowColumns);
        var accounts = Card();
        accounts.AddView(Label($"Accounts · Net worth {Amount(snapshot.NetWorth)}", 17, bold: true));
        accounts.AddView(Action(accountId is null ? "All accounts ✓" : "Show all accounts", () => { accountId = null; pageOffset = 0; RefreshDashboard(); }));
        foreach (var account in snapshot.Accounts)
        {
            accounts.AddView(Action($"{(accountId == account.Id ? "● " : string.Empty)}{account.Name}   {Amount(account.Balance)}", () => { accountId = account.Id; pageOffset = 0; RefreshDashboard(); }));
        }
        var categories = Card();
        categories.AddView(Action(categoryId is null ? "Categories · All  ›" : "Categories · Filtered  ›", () =>
        {
            var choices = snapshot.Categories.ToArray();
            new AlertDialog.Builder(this).SetTitle("Categories")!.SetItems(new[] { "All" }.Concat(choices.Select(c => c.Path)).ToArray(), (_, args) =>
            {
                categoryId = args.Which == 0 ? null : choices[args.Which - 1].Id;
                pageOffset = 0;
                RefreshDashboard();
            })!.Show();
        }));
        categories.AddView(Label("Top expenditures", 17, bold: true));
        foreach (var category in snapshot.LargestCategories)
        {
            categories.AddView(Label($"{category.Name}   {Amount(category.Amount)}", 15));
            var bar = new ProgressBar(this, null, global::Android.Resource.Attribute.ProgressBarStyleHorizontal)
            {
                Max = 1000,
                Progress = snapshot.MonthlyExpenses.Centimes == 0 ? 0 : (int)(category.Amount.Centimes * 1000m / snapshot.MonthlyExpenses.Centimes),
            };
            categories.AddView(bar);
        }
        var transactions = Card();
        search = new EditText(this) { Hint = "Search descriptions", Text = filter.Description, TextSize = 15 };
        search.SetSingleLine(true);
        search.SetTextColor(ViewerPalette.Text);
        search.SetHintTextColor(ViewerPalette.Muted);
        transactions.AddView(search);
        transactions.AddView(Action("Search", () => { pageOffset = 0; SearchTransactions(); }));
        var page = viewerReader.ReadTransactions(filter, pageOffset, PageSize);
        transactions.AddView(Label($"TRANSACTIONS ({page.TotalCount})", 12, ViewerPalette.Muted));
        foreach (var hit in page.Hits)
        {
            var entry = hit.Entry;
            var color = entry.Draft.Kind == TransactionKind.Income ? ViewerPalette.Positive : entry.Draft.Kind == TransactionKind.Expense ? ViewerPalette.Negative : ViewerPalette.Accent;
            var row = Column();
            row.AddView(Label($"{entry.Draft.Date:dd MMM}   {entry.Draft.Description}", 16, bold: true));
            row.AddView(Label($"{entry.CategoryPath ?? entry.Draft.Kind.ToString()} · {entry.AccountName}" + (entry.DestinationName is null ? string.Empty : $" → {entry.DestinationName}"), 12, ViewerPalette.Muted));
            row.AddView(Label($"{(entry.Draft.Kind == TransactionKind.Expense ? "−" : entry.Draft.Kind == TransactionKind.Income ? "+" : "↔")} {Amount(entry.Draft.Amount)}", 16, color));
            row.Click += (_, _) => new AlertDialog.Builder(this).SetTitle(entry.Draft.Description)!.SetMessage($"{entry.Draft.Date:dd MMM yyyy}\n{entry.AccountName}\n{entry.CategoryPath}\n{entry.Draft.Memo}")!.SetPositiveButton("Close", (_, _) => { })!.Show();
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

    private void ShowReminders()
    {
        var reminders = viewerReader!.ReadReminders();
        var today = DateOnly.FromDateTime(DateTime.Today);
        var total = reminders.Aggregate(Money.Zero, (sum, reminder) => sum + reminder.Template.IndicativeAmount);
        var summary = Card();
        summary.AddView(Label("Upcoming", 20, bold: true));
        summary.AddView(Label(Amount(total), 30, bold: true));
        summary.AddView(Label($"{reminders.Count} reminders · amounts are indicative", 13, ViewerPalette.Muted));
        foreach (var group in reminders.GroupBy(r => r.Overdue ? "Overdue" : r.Occurrence <= today.AddDays(5) ? "Due soon" : ReminderSchedule.MonthsUntil(r.Occurrence, today) == 1 ? "Next month" : "Later"))
        {
            content.AddView(Label(group.Key, 18, group.Key is "Overdue" or "Due soon" ? ViewerPalette.Warning : ViewerPalette.Positive, true));
            foreach (var reminder in group)
            {
                var card = Card();
                card.AddView(Label($"{reminder.Occurrence:dd MMM yyyy}   {reminder.Template.Description}", 17, bold: true));
                card.AddView(Label(Amount(reminder.Template.IndicativeAmount), 21, reminder.Overdue ? ViewerPalette.Negative : ViewerPalette.Accent));
                var days = reminder.Occurrence.DayNumber - today.DayNumber;
                card.AddView(Label(days < 0 ? $"{-(long)days} days overdue" : days == 0 ? "Due today" : $"in {days} days", 13, ViewerPalette.Muted));
            }
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
            controls.AddView(Action(expenses ? "Income" : "● Income", () => { expenses = false; RefreshDashboard(); }));
            controls.AddView(Action(expenses ? "● Expenses" : "Expenses", () => { expenses = true; RefreshDashboard(); }));
        }
        content.AddView(controls);
        var analytics = viewerReader!.ReadFlowAnalytics(filter);
        var card = Card();
        card.AddView(Label(tab == "Trend" ? "● Income    ● Expenses    ● Savings" : $"Cumulative {(expenses ? "expenses" : "income")}", 14, ViewerPalette.Muted));
        var detail = Label("Tap the chart to inspect a date or interval", 16);
        var chart = new ViewerChart(this, analytics, interval, tab == "Timeline", expenses, detail);
        card.AddView(chart, new LinearLayout.LayoutParams(-1, Dp(340)));
        if (tab == "Timeline") { card.AddView(Label($"Solid: {analytics.From:dd MMM} – {analytics.To:dd MMM}\nDashed: {analytics.PreviousFrom:dd MMM} – {analytics.PreviousTo:dd MMM}", 12, ViewerPalette.Muted)); }
        card.AddView(detail);
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
        background.SetCornerRadius(Dp(14));
        background.SetStroke(Dp(1), ViewerPalette.Border);
        return background;
    }
}
