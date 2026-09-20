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
    private const int HistoryPageSize = 100;
    private readonly LedgerStore _store;
    private LedgerSnapshot? _snapshot;
    private HistoryPage? _historyPage = null;
    private HistoryPage? _overviewHistory;
    private int _overviewOffset;
    private IReadOnlyList<RecurringReminder> _reminders = [];
    private HistoryFilter _historyFilter = new();
    private HistoryFilter _overviewFilter = new();
    private bool _overviewFiltersVisible;
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
            await Task.Run(() =>
            {
                Directory.CreateDirectory(directory);
                _store.Initialize();
            });
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
        if (_page == "Overview")
        {
            var filter = _overviewFilter with
            {
                From = _overviewFilter.From ?? from,
                To = _overviewFilter.To ?? to
            };
            _overviewHistory = await Task.Run(() => _store.ReadHistory(filter, _overviewOffset, HistoryPageSize));
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
        catch (Exception ex)
        {
            Status.Text = FriendlyError(ex);
            await ShowErrorDialog("Balancia", FriendlyError(ex));
        }
        finally
        {
            _busy = false;
            PageBody.IsEnabled = true;
            ResponsiveBody.IsEnabled = true;
            Navigation.IsEnabled = true;
            HeaderActions.IsEnabled = true;
        }
    }

    private async void ShowOverview(object? sender, RoutedEventArgs e) => await Navigate("Overview");
    private async void ShowCategories(object? sender, RoutedEventArgs e) => await Navigate("Categories");
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
        PageTitle.Text = _page == "Categories" ? "Settings" : _page;
        PageTitle.IsVisible = _page != "Overview";
        HeaderActions.IsVisible = _page == "Categories";

        foreach (var child in Navigation.Children.OfType<Button>())
        {
            child.Classes.Set("selected", Equals(child.Content, _page == "Categories" ? "Settings" : _page));
        }
        HeaderActions.Children.Clear();

        if (_page == "Categories")
        {
            HeaderActions.Children.Add(ActionButton("Import CSV", ImportCsv));
            HeaderActions.Children.Add(ActionButton("Export CSV", ExportCsv));
            HeaderActions.Children.Add(ActionButton("Export snapshot", ExportSnapshot));
            HeaderActions.Children.Add(ActionButton("Restore snapshot", RestoreSnapshot));
        }

        var responsive = _page is "Overview" or "Categories";
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
            case "Categories":
                RenderCategories(s);
                break;
        }
    }
}
