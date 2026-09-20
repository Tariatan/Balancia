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

public partial class MainWindow : Window
{
    private enum OverviewPeriod
    {
        All, ThisWeek, ThisMonth, ThisYear, Custom
    }
    private const int HistoryPageSize = 1000;
    private readonly LedgerStore _store;
    private LedgerSnapshot? _snapshot;
    private HistoryPage? _historyPage;
    private HistoryPage? _overviewHistory;
    private IReadOnlyList<RecurringReminder> _reminders = [];
    private HistoryFilter _historyFilter = new();
    private int _historyOffset;
    private readonly List<(DateOnly Date, string Id)> _historyCursors = [];
    private bool _historyUseOffsetPaging;
    private string? _pendingHistoryId;
    private string _page = "Overview";
    private bool _busy;
    private DateOnly _displayDate = DateOnly.FromDateTime(DateTime.Today);
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(30) };
    private string? _lastAccountId;
    private OverviewPeriod _overviewPeriod = OverviewPeriod.ThisMonth;
    private DateOnly? _customFrom;
    private DateOnly? _customTo;

    public MainWindow()
    {
        InitializeComponent();
        var args = Environment.GetCommandLineArgs();
        var directoryArg = Array.IndexOf(args, "--data-dir");
        var directory = directoryArg >= 0 && directoryArg + 1 < args.Length
            ? Path.GetFullPath(args[directoryArg + 1])
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Balancia");
        _store = new LedgerStore(Path.Combine(directory, "balancia.db"));
        if (directoryArg >= 0)
        {
            Title = "Balancia — Separate data folder";
        }

        Opened += async (_, _) => await Run(async () =>
        {
            await Task.Run(() => { Directory.CreateDirectory(directory); _store.Initialize(); });
            await Refresh();
        });
        _timer.Tick += async (_, _) =>
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            if (!_busy && today != _displayDate)
            {
                await Run(Refresh);
            }
        };
        Opened += (_, _) => _timer.Start();
        Closed += (_, _) => _timer.Stop();
    }

    private async Task Refresh()
    {
        _displayDate = DateOnly.FromDateTime(DateTime.Today);
        var (from, to) = OverviewRange();
        _snapshot = await Task.Run(() => _page == "Overview" ? _store.ReadDesktopSnapshotForPeriod(from, to) : _store.ReadDesktopSnapshot());
        _reminders = await Task.Run(() => _store.ReadRecurringReminders());
        if (_page == "Transactions")
        {
            _historyPage = await Task.Run(() => _historyUseOffsetPaging ? _store.ReadHistory(_historyFilter, _historyOffset, HistoryPageSize) :
                _historyCursors.Count == 0 ? _store.ReadHistory(_historyFilter, 0, HistoryPageSize) :
                _store.ReadHistoryAfter(_historyFilter, _historyCursors[^1].Date, _historyCursors[^1].Id, HistoryPageSize));
        }

        if (_page == "Overview")
        {
            _overviewHistory = await Task.Run(() => _store.ReadAllHistory(new HistoryFilter(From: from, To: to)));
        }

        Render();
    }

    private async Task Run(Func<Task> action)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        PageBody.IsEnabled = false;
        ResponsiveBody.IsEnabled = false;
        Navigation.IsEnabled = false;
        HeaderActions.IsEnabled = false;
        Status.Text = "Working…";
        try
        {
            await action();
            Status.Text = "Saved locally";
        }
        catch (Exception ex) { Status.Text = FriendlyError(ex); await ShowErrorDialog("Balancia", FriendlyError(ex)); }
        finally { _busy = false; PageBody.IsEnabled = true; ResponsiveBody.IsEnabled = true; Navigation.IsEnabled = true; HeaderActions.IsEnabled = true; }
    }

    private async void ShowOverview(object? sender, RoutedEventArgs e) => await Navigate("Overview");
    private async void ShowAccounts(object? sender, RoutedEventArgs e) => await Navigate("Accounts");
    private async void ShowCategories(object? sender, RoutedEventArgs e) => await Navigate("Categories");
    private async void ShowTransactions(object? sender, RoutedEventArgs e) => await Navigate("Transactions");
    private async Task Navigate(string page)
    {
        if (_page == page)
        {
            return;
        }
        _page = page;
        await Run(Refresh);
    }

    private void Render()
    {
        PageTitle.Text = _page;

        foreach (var child in Navigation.Children.OfType<Button>())
        {
            child.Classes.Set("selected", Equals(child.Content, _page));
        }
        HeaderActions.Children.Clear();

        if (_page == "Overview")
        {
            HeaderActions.Children.Add(ActionButton("Search", () => Navigate("Transactions")));
            var add = ActionButton("+ Transaction", () => EditTransaction(null));
            add.Background = Brush.Parse("#3989A7");
            add.Foreground = Brushes.White;
            HeaderActions.Children.Add(add);
        }
        else if (_page == "Transactions")
        {
            HeaderActions.Children.Add(ActionButton("Import CSV", ImportCsv));
            HeaderActions.Children.Add(ActionButton("Export CSV", ExportCsv));
            HeaderActions.Children.Add(ActionButton("Export snapshot", ExportSnapshot));
            HeaderActions.Children.Add(ActionButton("Restore snapshot", RestoreSnapshot));
            var add = ActionButton("+ Transaction", () => EditTransaction(null));
            add.Background = Brush.Parse("#3989A7");
            add.Foreground = Brushes.White;
            HeaderActions.Children.Add(add);
        }

        var responsive = _page is "Overview" or "Transactions" or "Categories";
        PageScrollViewer.IsVisible = !responsive;
        ResponsiveBody.IsVisible = responsive;
        ResponsiveBody.Content = null;
        PageBody.Spacing = 18;
        PageBody.Children.Clear();

        if (_snapshot is not { } s)
        {
            var message = Text("The ledger could not be loaded. Check the message below and restart after resolving it.");
            if (responsive)
            {
                ResponsiveBody.Content = message;
            }
            else
            {
                PageBody.Children.Add(message);
            }

            return;
        }

        switch (_page)
        {
            case "Overview":
                RenderOverview(s);
                break;
            case "Accounts":
                PageBody.Children.Add(Text("Set a dated opening balance. Archive accounts to stop new entries without losing history."));
                var accounts = new ListBox
                {
                    ItemsSource = s.Accounts.Select(a => new Choice<Account>(a, $"{a}    {AmountText(a.Balance)}")).ToArray(),
                    MinHeight = 100,
                    MaxHeight = 300
                };
                PageBody.Children.Add(accounts);
                PageBody.Children.Add(
                    Row(
                        ActionButton("Add account", () => EditAccount(null)),
                        ActionButton("Edit selected account", () => accounts.SelectedItem is Choice<Account> a ? EditAccount(a.Value) : SelectFirst())
                        )
                    );
                break;
            case "Categories":
                RenderCategories(s);
                break;
            case "Transactions":
                RenderTransactions(s);
                break;
        }
    }
}
