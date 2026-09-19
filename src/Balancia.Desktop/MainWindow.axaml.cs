using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
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

public partial class MainWindow : Window
{
    private readonly LedgerStore _store;
    private LedgerSnapshot? _snapshot;
    private HistoryPage? _historyPage;
    private HistoryPage? _recentPage;
    private HistoryFilter _historyFilter = new();
    private int _historyOffset;
    private readonly List<(DateOnly Date, string Id)> _historyCursors = [];
    private string _page = "Overview";
    private bool _busy;
    private DateOnly _displayDate = DateOnly.FromDateTime(DateTime.Today);
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(30) };
    private string? _lastAccountId;

    public MainWindow()
    {
        InitializeComponent();
        var args = Environment.GetCommandLineArgs();
        var directoryArg = Array.IndexOf(args, "--data-dir");
        var directory = directoryArg >= 0 && directoryArg + 1 < args.Length
            ? Path.GetFullPath(args[directoryArg + 1])
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Balancia");
        _store = new LedgerStore(Path.Combine(directory, "balancia.db"));
        if (directoryArg >= 0) Title = "Balancia — Separate data folder";
        Opened += async (_, _) => await Run(async () =>
        {
            await Task.Run(() => { Directory.CreateDirectory(directory); _store.Initialize(); });
            await Refresh();
        });
        _timer.Tick += async (_, _) =>
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            if (!_busy && today != _displayDate) await Run(Refresh);
        };
        Opened += (_, _) => _timer.Start();
        Closed += (_, _) => _timer.Stop();
    }

    private async Task Refresh()
    {
        _snapshot = await Task.Run(_store.ReadDesktopSnapshot);
        if (_page == "Transactions")
            _historyPage = await Task.Run(() => _historyCursors.Count == 0 ? _store.ReadHistory(_historyFilter) :
                _store.ReadHistoryAfter(_historyFilter, _historyCursors[^1].Date, _historyCursors[^1].Id));
        if (_page == "Overview") _recentPage = await Task.Run(() => _store.ReadHistory(new HistoryFilter(), 0, 5));
        _displayDate = DateOnly.FromDateTime(DateTime.Today);
        Render();
    }

    private async Task Run(Func<Task> action)
    {
        if (_busy) return;
        _busy = true; PageBody.IsEnabled = false; Navigation.IsEnabled = false;
        Status.Text = "Working…";
        try { await action(); Status.Text = "Saved locally · CHF"; }
        catch (Exception ex) { Status.Text = FriendlyError(ex); }
        finally { _busy = false; PageBody.IsEnabled = true; Navigation.IsEnabled = true; }
    }

    private static string FriendlyError(Exception ex) => ex switch
    {
        SqliteException { SqliteErrorCode: 19 } => "This change conflicts with existing data. Check names and referenced accounts/categories.",
        SqliteException => "The database could not be read or saved. Close other Balancia windows and try again.",
        OverflowException => "This amount or resulting total is outside the supported range.",
        FormatException => "Check the date (YYYY-MM-DD) and amount (for example 12.50).",
        IOException or UnauthorizedAccessException => "The local data folder is unavailable or not writable.",
        ArgumentException or InvalidOperationException => ex.Message,
        _ => "The operation failed. Your entered values have been kept; try again."
    };

    private async void ShowOverview(object? sender, RoutedEventArgs e) => await Navigate("Overview");
    private async void ShowAccounts(object? sender, RoutedEventArgs e) => await Navigate("Accounts");
    private async void ShowCategories(object? sender, RoutedEventArgs e) => await Navigate("Categories");
    private async void ShowTransactions(object? sender, RoutedEventArgs e) => await Navigate("Transactions");
    private async void ShowRecurring(object? sender, RoutedEventArgs e) => await Navigate("Recurring payments");
    private async Task Navigate(string page) { _page = page; await Run(Refresh); }

    private void Render()
    {
        PageTitle.Text = _page;
        PageBody.Children.Clear();
        if (_snapshot is not { } s) { PageBody.Children.Add(Text("The ledger could not be loaded. Check the message below and restart after resolving it.")); return; }
        switch (_page)
        {
            case "Overview":
                PageBody.Children.Add(Text($"{_displayDate:MMMM yyyy} · All accounts, including archived accounts"));
                PageBody.Children.Add(Card("Net worth", Chf(s.NetWorth)));
                PageBody.Children.Add(Card("This month's income / expenses", $"{Chf(s.MonthlyIncome)}  /  {Chf(s.MonthlyExpenses)}"));
                PageBody.Children.Add(Card("Largest expense categories", s.LargestCategories.Count == 0 ? "No expenses this month." : string.Join("\n", s.LargestCategories.Select(c => $"{c.Name}    {Chf(c.Amount)}"))));
                PageBody.Children.Add(Card("Upcoming payments", "Recurring payment reminders are not available yet."));
                PageBody.Children.Add(Card("Transaction history", _recentPage!.Hits.Count == 0 ? "No transactions yet. Start by adding an account." : string.Join("\n", _recentPage.Hits.Select(h => EntryText(h.Entry)))));
                PageBody.Children.Add(ActionButton("Add account", () => EditAccount(null)));
                break;
            case "Accounts":
                PageBody.Children.Add(Text("Set a dated opening balance. Archive accounts to stop new entries without losing history."));
                var accounts = new ListBox { ItemsSource = s.Accounts.Select(a => new Choice<Account>(a, $"{a}    {Chf(a.Balance)}")).ToArray(), MinHeight = 100, MaxHeight = 300 };
                PageBody.Children.Add(accounts);
                PageBody.Children.Add(Row(ActionButton("Add account", () => EditAccount(null)), ActionButton("Edit selected account", () => accounts.SelectedItem is Choice<Account> a ? EditAccount(a.Value) : SelectFirst())));
                break;
            case "Categories":
                PageBody.Children.Add(Text("Choose an optional parent for a subcategory. Archiving a parent also archives its children."));
                var categories = new ListBox { ItemsSource = s.Categories, MinHeight = 100, MaxHeight = 300 };
                PageBody.Children.Add(categories);
                PageBody.Children.Add(Row(ActionButton("Add category", () => EditCategory(null)), ActionButton("Edit selected category", () => categories.SelectedItem is Category c ? EditCategory(c) : SelectFirst())));
                break;
            case "Transactions":
                PageBody.Children.Add(Text("Newest first · Search and filters combine. Date and amount endpoints are inclusive."));
                var search = Input(_historyFilter.Description ?? "");
                var accountChoices = new List<Choice<string?>> { new(null, "All accounts") };
                accountChoices.AddRange(s.Accounts.Select(a => new Choice<string?>(a.Id, a.Name)));
                var accountFilter = new ComboBox { ItemsSource = accountChoices, SelectedItem = accountChoices.FirstOrDefault(c => c.Value == _historyFilter.AccountId) ?? accountChoices[0], Width = 180 };
                var typeChoices = new List<Choice<TransactionKind?>> { new(null, "All types") };
                typeChoices.AddRange(Enum.GetValues<TransactionKind>().Select(k => new Choice<TransactionKind?>(k, k.ToString())));
                var typeFilter = new ComboBox { ItemsSource = typeChoices, SelectedItem = typeChoices.FirstOrDefault(c => c.Value == _historyFilter.Kind) ?? typeChoices[0], Width = 150 };
                var categoryChoices = new List<Choice<string?>> { new(null, "All categories") };
                categoryChoices.AddRange(s.Categories.Select(c => new Choice<string?>(c.Id, c.Path)));
                var categoryFilter = new ComboBox { ItemsSource = categoryChoices, SelectedItem = categoryChoices.FirstOrDefault(c => c.Value == _historyFilter.CategoryId) ?? categoryChoices[0], Width = 220 };
                var from = Input(_historyFilter.From?.ToString("yyyy-MM-dd") ?? ""); from.Width = 130;
                var to = Input(_historyFilter.To?.ToString("yyyy-MM-dd") ?? ""); to.Width = 130;
                var minimum = Input(_historyFilter.Minimum?.Francs.ToString("0.00", CultureInfo.InvariantCulture) ?? ""); minimum.Width = 110;
                var maximum = Input(_historyFilter.Maximum?.Francs.ToString("0.00", CultureInfo.InvariantCulture) ?? ""); maximum.Width = 110;
                PageBody.Children.Add(Field("Description contains", search));
                PageBody.Children.Add(Row(Field("Account", accountFilter), Field("Type", typeFilter), Field("Category / subcategory", categoryFilter)));
                PageBody.Children.Add(Row(Field("From (YYYY-MM-DD)", from), Field("To (YYYY-MM-DD)", to),
                    Field("Min CHF", minimum), Field("Max CHF", maximum)));
                async Task ApplyFilters()
                {
                    await Run(async () =>
                    {
                        var proposed = new HistoryFilter(search.Text, ((Choice<string?>)accountFilter.SelectedItem!).Value,
                            ((Choice<TransactionKind?>)typeFilter.SelectedItem!).Value, ((Choice<string?>)categoryFilter.SelectedItem!).Value,
                            OptionalDate(from), OptionalDate(to), OptionalMoney(minimum), OptionalMoney(maximum));
                        if (proposed.From > proposed.To || proposed.Minimum?.Centimes < 0 || proposed.Maximum?.Centimes < 0 ||
                            proposed.Minimum is { } min && proposed.Maximum is { } max && min.Centimes > max.Centimes)
                            throw new ArgumentException("Check the date and amount range endpoints.");
                        _historyFilter = proposed;
                        _historyOffset = 0;
                        _historyCursors.Clear();
                        await Refresh();
                    });
                }
                search.KeyDown += async (_, e) => { if (e.Key == Key.Enter) await ApplyFilters(); };
                PageBody.Children.Add(Row(ActionButton("Apply filters", ApplyFilters),
                    ActionButton("Clear filters", async () => { _historyFilter = new(); _historyOffset = 0; _historyCursors.Clear(); await Run(Refresh); })));
                var page = _historyPage!;
                PageBody.Children.Add(Text($"{page.TotalCount:N0} matching transactions · Showing {(_historyOffset == 0 && page.Hits.Count == 0 ? 0 : _historyOffset + 1):N0}–{(_historyOffset + page.Hits.Count):N0}"));
                var entries = new ListBox { ItemsSource = page.Hits.Select(h => new Choice<HistoryHit>(h,
                    EntryText(h.Entry) + (h.AccountEffect is { } effect ? $"  ·  This account: {Chf(effect)}" : ""))).ToArray(), MinHeight = 180, MaxHeight = 380 };
                PageBody.Children.Add(entries);
                PageBody.Children.Add(Row(ActionButton("Previous page", async () =>
                    { if (_historyCursors.Count > 0) { _historyCursors.RemoveAt(_historyCursors.Count - 1); _historyOffset -= 100; await Run(Refresh); } }),
                    ActionButton("Next page", async () =>
                    { if (_historyOffset + page.Hits.Count < page.TotalCount && page.Hits.Count > 0)
                        { var last = page.Hits[^1].Entry; _historyCursors.Add((last.Draft.Date, last.Id)); _historyOffset += page.Hits.Count; await Run(Refresh); } })));
                PageBody.Children.Add(Row(ActionButton("Add transaction", () => EditTransaction(null)),
                    ActionButton("Edit selected transaction", () => entries.SelectedItem is Choice<HistoryHit> e ? EditTransaction(e.Value.Entry) : SelectFirst()),
                    ActionButton("Remove selected transaction", () => entries.SelectedItem is Choice<HistoryHit> e ? RemoveTransaction(e.Value.Entry) : SelectFirst()),
                    ActionButton("Import Buxfer CSV", ImportBuxfer)));
                break;
            default: PageBody.Children.Add(Card("Recurring payments", "Reminder editing is not available yet. Transactions are never created automatically.")); break;
        }
    }

    private Task SelectFirst() { Status.Text = "Select a row first."; return Task.CompletedTask; }
    private async Task ImportBuxfer()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose Buxfer transactions CSV", AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("CSV") { Patterns = ["*.csv"] }]
        });
        if (files.Count == 0) return;
        var path = files[0].TryGetLocalPath();
        if (path is null) { Status.Text = "Choose a local CSV file."; return; }
        await Run(async () =>
        {
            var preview = await Task.Run(() => _store.PreviewBuxfer(path));
            var summary = preview.Summary;
            var message = $"{summary.Rows} rows: {summary.Expenses} expenses, {summary.Incomes} income, {summary.Transfers} paired transfers, {summary.Openings} openings.\n\n" +
                "Resolved dates (first 10 rows, 2000–2099): " +
                string.Join(", ", preview.ResolvedDates.Select(d => $"line {d.Line}: {d.Date:yyyy-MM-dd}")) +
                "\n\nAccounts and source totals (CHF):\n" +
                string.Join("\n", summary.AccountTotals.Select(a => $"{a.Account}: {a.Centimes / 100m:N2}")) +
                "\n\nCategories: " + string.Join(", ", summary.Categories) +
                (preview.Issues.Count == 0 ? "\n\nAll rows are valid. Apply this batch?" :
                    "\n\nErrors (correct source file, then preview again):\n" +
                    string.Join("\n", preview.Issues.Take(30).Select(i => $"Line {i.Line}: {i.Message}")));
            var dialog = new Window { Title = "Buxfer import preview", Width = 700, Height = 650,
                MinWidth = 500, MinHeight = 350, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var body = new StackPanel { Spacing = 12, Margin = new Thickness(20) };
            body.Children.Add(Text(message));
            var apply = new Button { Content = "Apply import", IsEnabled = preview.CanApply };
            var cancel = new Button { Content = "Cancel", IsCancel = true };
            var error = Text(""); error.Foreground = Brushes.DarkRed;
            body.Children.Add(error); body.Children.Add(Row(apply, cancel));
            dialog.Content = new ScrollViewer { Content = body };
            cancel.Click += (_, _) => dialog.Close();
            apply.Click += async (_, _) =>
            {
                apply.IsEnabled = false;
                try
                {
                    var result = await Task.Run(() => _store.ApplyBuxfer(preview));
                    dialog.Close();
                    _historyCursors.Clear(); _historyOffset = 0;
                    await Refresh();
                    Status.Text = $"Imported {result.Added} entries; {result.Unchanged} unchanged.";
                }
                catch (Exception ex) { error.Text = FriendlyError(ex); apply.IsEnabled = true; }
            };
            await dialog.ShowDialog(this);
        });
    }
    private async Task EditAccount(Account? account)
    {
        var name = Input(account?.Name ?? "");
        var date = Input((account?.OpeningDate ?? _displayDate).ToString("yyyy-MM-dd"));
        var amount = Input((account?.OpeningAmount.Francs ?? 0).ToString("0.00", CultureInfo.InvariantCulture));
        var archived = new CheckBox { Content = "Archived", IsChecked = account?.Archived ?? false };
        await EditDialog(account is null ? "Add account" : "Edit account", [Field("Name", name), Field("Opening date (YYYY-MM-DD)", date), Field("Opening amount (CHF)", amount), archived],
            () => { var values = (name.Text ?? "", ParseDate(date), ParseMoney(amount), archived.IsChecked == true); return () => _store.SaveAccount(account?.Id, values.Item1, values.Item2, values.Item3, values.Item4); });
    }

    private async Task EditCategory(Category? category)
    {
        var name = Input(category?.Name ?? "");
        var options = new List<Choice<string?>> { new(null, "No parent (top-level)") };
        options.AddRange(_snapshot!.Categories.Where(c => c.ParentId is null && !c.Archived && c.Id != category?.Id).Select(c => new Choice<string?>(c.Id, c.Path)));
        if (category?.ParentId is { } current && options.All(c => c.Value != current))
            options.Add(new(current, _snapshot.Categories.Single(c => c.Id == current).ToString()));
        var parent = new ComboBox { ItemsSource = options, SelectedItem = options.FirstOrDefault(c => c.Value == category?.ParentId) ?? options[0], HorizontalAlignment = HorizontalAlignment.Stretch };
        var archived = new CheckBox { Content = "Archived", IsChecked = category?.Archived ?? false };
        await EditDialog(category is null ? "Add category" : "Edit category", [Field("Name", name), Field("Parent category", parent), archived],
            () => { var values = (name.Text ?? "", ((Choice<string?>)parent.SelectedItem!).Value, archived.IsChecked == true); return () => _store.SaveCategory(category?.Id, values.Item1, values.Item2, values.Item3); });
    }

    private async Task EditTransaction(LedgerEntry? entry)
    {
        var existing = entry?.Draft;
        var accounts = _snapshot!.Accounts.Where(a => !a.Archived || a.Id == existing?.AccountId || a.Id == existing?.DestinationId).ToArray();
        if (accounts.Length == 0) { Status.Text = "Add an active account before entering transactions."; return; }
        var kind = new ComboBox { ItemsSource = Enum.GetValues<TransactionKind>(), SelectedItem = existing?.Kind ?? TransactionKind.Expense, HorizontalAlignment = HorizontalAlignment.Stretch };
        var date = Input((existing?.Date ?? _displayDate).ToString("yyyy-MM-dd"));
        var description = Input(existing?.Description ?? "");
        var amount = Input(existing?.Amount.Francs.ToString("0.00", CultureInfo.InvariantCulture) ?? "");
        var account = new ComboBox { ItemsSource = accounts, SelectedItem = accounts.FirstOrDefault(a => a.Id == (existing?.AccountId ?? _lastAccountId)) ?? accounts[0], HorizontalAlignment = HorizontalAlignment.Stretch };
        var destination = new ComboBox { ItemsSource = accounts, SelectedItem = accounts.FirstOrDefault(a => a.Id == existing?.DestinationId), HorizontalAlignment = HorizontalAlignment.Stretch };
        var choices = new List<Choice<string?>> { new(null, "Uncategorized") };
        choices.AddRange(_snapshot.Categories.Where(c => !c.Archived || c.Id == existing?.CategoryId).Select(c => new Choice<string?>(c.Id, c.ToString())));
        var category = new ComboBox { ItemsSource = choices, SelectedItem = choices.FirstOrDefault(c => c.Value == existing?.CategoryId) ?? choices[0], HorizontalAlignment = HorizontalAlignment.Stretch };
        var categorySearch = Input("");
        categorySearch.PlaceholderText = "Type to narrow categories";
        categorySearch.TextChanged += (_, _) =>
        {
            var selected = category.SelectedItem as Choice<string?>;
            var visible = choices.Where(c => c.Value is null || c.Label.Contains(categorySearch.Text ?? "", StringComparison.OrdinalIgnoreCase)).ToArray();
            category.ItemsSource = visible;
            category.SelectedItem = visible.FirstOrDefault(c => c.Value == selected?.Value) ?? visible[0];
        };
        var memo = Input(existing?.Memo ?? "");
        var toField = Field("Destination account", destination);
        var categoryField = Field("Category / subcategory", new StackPanel { Spacing = 5, Children = { categorySearch, category } });
        void UpdateFields() { var transfer = kind.SelectedItem is TransactionKind.Transfer; toField.IsVisible = transfer; categoryField.IsVisible = !transfer; }
        kind.SelectionChanged += (_, _) => UpdateFields(); UpdateFields();
        await EditDialog(entry is null ? "Add transaction" : "Edit transaction",
            [Field("Type", kind), Field("Date (YYYY-MM-DD)", date), Field("Description", description), Field("Amount (CHF, positive)", amount), Field("Account", account), toField, categoryField, Field("Notes", memo)],
            () =>
            {
                var type = (TransactionKind)kind.SelectedItem!;
                var draft = new TransactionDraft(type, ParseDate(date), description.Text ?? "", ParseMoney(amount), ((Account)account.SelectedItem!).Id,
                    type == TransactionKind.Transfer ? (destination.SelectedItem as Account)?.Id : null,
                    type == TransactionKind.Transfer ? null : ((Choice<string?>)category.SelectedItem!).Value, memo.Text ?? "");
                _lastAccountId = draft.AccountId;
                return () => _store.SaveTransaction(entry?.Id, draft);
            });
    }

    private async Task RemoveTransaction(LedgerEntry entry) => await EditDialog("Remove transaction",
        [Text(EntryText(entry)), Text("Remove this transaction? For a transfer, both account movements will be removed together.")],
        () => () => _store.DeleteTransaction(entry.Id), "Remove");

    // Snapshot inputs on the UI thread, then perform the complete write off-thread.
    private async Task EditDialog(string title, Control[] fields, Func<Action> prepareSave, string saveLabel = "Save")
    {
        var dialog = new Window { Title = title, Width = 530, Height = title.Contains("transaction", StringComparison.OrdinalIgnoreCase) ? 730 : 480,
            MinWidth = 430, MinHeight = 360, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Brushes.White };
        var body = new StackPanel { Spacing = 12, Margin = new Thickness(24) };
        body.Children.Add(new TextBlock { Text = title, FontSize = 24, FontWeight = FontWeight.SemiBold });
        foreach (var field in fields) body.Children.Add(field);
        var error = Text(""); error.Foreground = Brushes.DarkRed;
        body.Children.Add(error);
        var save = new Button { Content = saveLabel, IsDefault = saveLabel == "Save" };
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        body.Children.Add(Row(save, cancel));
        dialog.Content = new ScrollViewer { Content = body };
        var saving = false;
        cancel.Click += (_, _) => dialog.Close();
        dialog.Closing += (_, e) => { if (saving) e.Cancel = true; };
        dialog.KeyDown += (_, e) => { if (e.Key == Key.Escape && !saving) dialog.Close(); };
        save.Click += async (_, _) =>
        {
            if (saving) return;
            try
            {
                var action = prepareSave();
                saving = true; body.IsEnabled = false; error.Text = "Saving…";
                await Task.Run(action);
                saving = false;
                _historyCursors.Clear(); _historyOffset = 0;
                dialog.Close();
            }
            catch (Exception ex) { error.Text = FriendlyError(ex); }
            finally { saving = false; body.IsEnabled = true; }
        };
        dialog.Opened += (_, _) => { if (fields.FirstOrDefault() is StackPanel panel && panel.Children.LastOrDefault() is InputElement input) input.Focus(); };
        await dialog.ShowDialog(this);
        await Run(Refresh);
    }

    private static Money ParseMoney(TextBox input) => Money.FromFrancs(decimal.Parse(input.Text ?? "", NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite, CultureInfo.InvariantCulture));
    private static Money? OptionalMoney(TextBox input) => string.IsNullOrWhiteSpace(input.Text) ? null : ParseMoney(input);
    private static DateOnly? OptionalDate(TextBox input) => string.IsNullOrWhiteSpace(input.Text) ? null : ParseDate(input);
    private static DateOnly ParseDate(TextBox input) => DateOnly.ParseExact(input.Text ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static TextBox Input(string text) => new() { Text = text, HorizontalAlignment = HorizontalAlignment.Stretch };
    private static TextBlock Text(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = Brush.Parse("#344D44") };
    private static string Chf(Money value) => "CHF " + value.Francs.ToString("N2", CultureInfo.GetCultureInfo("de-CH"));
    private static string EntryText(LedgerEntry entry) => $"{entry.Draft.Date:yyyy-MM-dd}  ·  {entry.Draft.Kind}  ·  {Chf(entry.Draft.Amount)}  ·  {entry.AccountName}{(entry.DestinationName is null ? "" : " → " + entry.DestinationName)}  ·  {entry.Draft.Description}  ·  {entry.CategoryPath ?? ""}";
    private static StackPanel Field(string label, Control input)
    {
        AutomationProperties.SetName(input, label);
        return new() { Spacing = 5, Children = { Text(label), input } };
    }
    private static WrapPanel Row(params Control[] controls)
    {
        var row = new WrapPanel(); foreach (var control in controls) { control.Margin = new Thickness(0, 0, 10, 10); row.Children.Add(control); } return row;
    }
    private static Border Card(string heading, string message) => new() { Background = Brushes.White, CornerRadius = new CornerRadius(10), Padding = new Thickness(20),
        Child = new StackPanel { Spacing = 10, Children = { new TextBlock { Text = heading, FontSize = 18, FontWeight = FontWeight.SemiBold }, Text(message) } } };
    private static Button ActionButton(string title, Func<Task> action)
    {
        var button = new Button { Content = title }; button.Click += async (_, _) => await action(); return button;
    }
    private sealed record Choice<T>(T Value, string Label) { public override string ToString() => Label; }
}
