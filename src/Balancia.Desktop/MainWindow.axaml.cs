using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Balancia.Core;
using Balancia.Storage;

namespace Balancia.Desktop;

public partial class MainWindow : Window
{
    private enum OverviewPeriod
    {
        All, ThisWeek, ThisMonth, ThisYear, Custom
    }
    private const int HistoryPageSize = 100;
    private readonly LedgerStore store;
    private readonly string windowSettingsPath;
    private LedgerSnapshot? snapshot;
    private HistoryPage? overviewHistory;
    private int overviewOffset;
    private IReadOnlyList<RecurringReminder> reminders = [];
    private HistoryFilter overviewFilter = new();
    private bool overviewFiltersVisible;
    private bool pendingOverviewFilterRefresh;
    private string page = "Overview";
    private bool busy;
    private DateOnly displayDate = DateOnly.FromDateTime(DateTime.Today);
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(30) };
    private string? lastAccountId;
    private OverviewPeriod overviewPeriod = OverviewPeriod.ThisMonth;
    private DateOnly? customFrom;
    private DateOnly? customTo;

    public MainWindow()
    {
        InitializeComponent();
        var args = Environment.GetCommandLineArgs();
        var directoryArg = Array.IndexOf(args, "--data-dir");
        var directory = directoryArg >= 0 && directoryArg + 1 < args.Length
            ? Path.GetFullPath(args[directoryArg + 1])
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Balancia");
        store = new LedgerStore(Path.Combine(directory, "balancia.db"));
        windowSettingsPath = Path.Combine(directory, "window.json");
        LoadWindowSettings();
        PositionChanged += (_, _) => windowPositionForPersistence = Position;
        if (directoryArg >= 0)
        {
            Title = "Balancia — Separate data folder";
        }

        Opened += (_, _) => ApplyLoadedWindowPosition();
        Opened += async (_, _) => await Run(async () =>
        {
            await Task.Run(() =>
            {
                Directory.CreateDirectory(directory);
                store.Initialize();
            });
            await Refresh();
        });
        timer.Tick += async (_, _) =>
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            if (!busy && today != displayDate)
            {
                await Run(Refresh);
            }
        };
        Opened += (_, _) => timer.Start();
        Closed += (_, _) => timer.Stop();
        Closed += (_, _) => SaveWindowSettings();
        KeyDown += async (_, e) =>
        {
            if (e.Handled || busy || snapshot is null || e.Key is not (Key.OemPlus or Key.Add) ||
                (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Alt)) != 0 ||
                e.Source is TextBox or AutoCompleteBox)
            {
                return;
            }

            e.Handled = true;
            await EditTransaction(null);
        };
    }

    private Task Refresh() => RefreshCore(false);

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

    private async Task RefreshCore(bool updateOverviewInPlace)
    {
        displayDate = DateOnly.FromDateTime(DateTime.Today);
        var (from, to) = OverviewRange();
        var filter = overviewFilter with
        {
            From = overviewFilter.From ?? from,
            To = overviewFilter.To ?? to
        };
        snapshot = await Task.Run(() => page == "Overview" ? store.ReadDesktopSnapshotForFilter(filter) : store.ReadDesktopSnapshot());
        reminders = await Task.Run(() => store.ReadRecurringReminders());
        if (page == "Overview")
        {
            overviewHistory = await Task.Run(() => store.ReadHistory(filter, overviewOffset));
        }

        if (updateOverviewInPlace && page == "Overview" && overviewLayout is not null)
        {
            UpdateOverviewInPlace();
        }
        else
        {
            Render();
        }
    }

    private async Task Run(Func<Task> action, bool disableControls = true)
    {
        if (busy)
        {
            return;
        }

        busy = true;
        if (disableControls)
        {
            PageBody.IsEnabled = false;
            ResponsiveBody.IsEnabled = false;
            Navigation.IsEnabled = false;
            HeaderActions.IsEnabled = false;
            Status.Text = "Working…";
        }
        try
        {
            await action();
            if (disableControls)
            {
                Status.Text = "Saved locally";
            }
        }
        catch (Exception ex)
        {
            Status.Text = FriendlyError(ex);
            await ShowErrorDialog("Balancia", FriendlyError(ex));
        }
        finally
        {
            busy = false;
            if (disableControls)
            {
                PageBody.IsEnabled = true;
                ResponsiveBody.IsEnabled = true;
                Navigation.IsEnabled = true;
                HeaderActions.IsEnabled = true;
            }

            if (pendingOverviewFilterRefresh)
            {
                pendingOverviewFilterRefresh = false;
                if (page == "Overview")
                {
                    Dispatcher.UIThread.Post(() => _ = RequestOverviewFilterRefresh());
                }
            }
        }
    }

    private async void ShowOverview(object? sender, RoutedEventArgs e) => await Navigate("Overview");
    private async void ShowCategories(object? sender, RoutedEventArgs e) => await Navigate("Categories");
    private async Task Navigate(string page)
    {
        if (this.page == page)
        {
            return;
        }
        this.page = page;
        await Run(Refresh);
    }

    private void Render()
    {
        PageTitle.Text = page == "Categories" ? "Settings" : page;
        PageTitle.IsVisible = page != "Overview";
        HeaderActions.IsVisible = page == "Categories";

        foreach (var child in Navigation.Children.OfType<Button>())
        {
            child.Classes.Set("selected", Equals(child.Content, page == "Categories" ? "Settings" : page));
        }
        HeaderActions.Children.Clear();

        if (page == "Categories")
        {
            HeaderActions.Children.Add(ActionButton("Import CSV", ImportCsv));
            HeaderActions.Children.Add(ActionButton("Export CSV", ExportCsv));
            HeaderActions.Children.Add(ActionButton("Export snapshot", ExportSnapshot));
            HeaderActions.Children.Add(ActionButton("Restore snapshot", RestoreSnapshot));
        }

        var responsive = page is "Overview" or "Categories";
        PageScrollViewer.IsVisible = !responsive;
        ResponsiveBody.IsVisible = responsive;
        overviewFilterFrom = null;
        overviewFilterTo = null;
        ResponsiveBody.Content = null;
        overviewLayout = null;
        PageBody.Spacing = 18;
        PageBody.Children.Clear();

        if (snapshot is not { } s)
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

        switch (page)
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
