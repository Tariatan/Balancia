using Avalonia.Controls;
using Avalonia.Input;
using System.Globalization;
using Avalonia.Threading;
using Balancia.Core;
using Balancia.Storage;

using static Balancia.Desktop.Localization.UiText;

namespace Balancia.Desktop;

public partial class MainWindow : Window
{
    internal LedgerStore store;
    internal string databasePath;
    private readonly string applicationSettingsPath;
    private readonly bool separateDataFolder;
    internal string? backupPath;
    internal string? snapshotPath;
    private string? defaultAccountId;
    internal string languageCode;
    private string? languagePreference;
    private LedgerSnapshot? snapshot;
    private FlowAverages averages = new(Money.Zero, Money.Zero);
    private IReadOnlyList<Reminder> reminders = [];
    private bool busy;
    private DateOnly displayDate = DateOnly.FromDateTime(DateTime.Today);
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(30) };
    private string? lastAccountId;
    private string statusMessage;

    internal void SetStatus(string text)
    {
        statusMessage = Get(text);
        OverviewStatus.Text = statusMessage;
    }

    public MainWindow()
    {
        InitializeComponent();
        InitializeOverview();
        var args = Environment.GetCommandLineArgs();
        var directoryArg = Array.IndexOf(args, "--data-dir");
        separateDataFolder = directoryArg >= 0 && directoryArg + 1 < args.Length;
        var userDirectory = ApplicationPaths.UserDirectory;
        applicationSettingsPath = ApplicationPaths.SettingsFile;
        var savedSettings = LoadApplicationSettings(applicationSettingsPath);
        var directory = separateDataFolder
            ? Path.GetFullPath(args[directoryArg + 1])
            : Path.GetDirectoryName(ResolveFullPath(savedSettings?.DatabasePath) ?? string.Empty) ?? userDirectory;
        languagePreference = savedSettings?.Language;
        languageCode = ResolveLanguage(savedSettings?.Language, CultureInfo.CurrentUICulture);
        SetLanguage(languageCode);
        statusMessage = Get("Loading…");
        backupPath = separateDataFolder ? null : ResolveFullPath(savedSettings?.BackupPath);
        snapshotPath = separateDataFolder ? null : ResolveFullPath(savedSettings?.SnapshotPath);
        defaultAccountId = separateDataFolder ? null : savedSettings?.DefaultAccountId;
        databasePath = Path.Combine(directory, "balancia.db");
        store = new LedgerStore(databasePath);
        LoadWindowSettings(savedSettings);
        PositionChanged += (_, _) => windowPositionForPersistence = Position;
        if (directoryArg >= 0)
        {
            Title = $"Balancia — {Get("Separate data folder")}";
        }

        Opened += (_, _) => ApplyLoadedWindowPosition();
        Opened += async (_, _) => await Run(async () =>
        {
            await Task.Run(() =>
            {
                Directory.CreateDirectory(directory);
                Serilog.Log.Information("Initializing ledger, DatabasePath: '{DatabasePath}', SeparateDataFolder: '{SeparateDataFolder}'", databasePath, separateDataFolder);
                store.Initialize();
                Serilog.Log.Information("Ledger initialized");
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
        Closed += (_, _) => SaveBackupOnClose();
        Closed += (_, _) => SaveSnapshotOnClose();
        KeyDown += async (_, e) =>
        {
            if (e.Handled || busy || snapshot is null ||
                (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Alt)) != 0 ||
                e.Source is TextBox or AutoCompleteBox)
            {
                return;
            }

            switch (e.Key)
            {
                case Key.OemPlus or Key.Add:
                    e.Handled = true;
                    await EditTransaction(null);
                    break;
                case Key.Delete when (TransactionsList?.SelectedItem is TransactionItem item):
                    e.Handled = true;
                    await RemoveTransaction(item.Hit.Entry);
                    break;
            }
        };
    }

    internal Task Refresh() => RefreshCore(false);

    private async Task RefreshCore(bool updateOverviewInPlace)
    {
        displayDate = DateOnly.FromDateTime(DateTime.Today);
        var (from, to) = OverviewRange();
        var filter = this.filter with
        {
            From = this.filter.From ?? from,
            To = this.filter.To ?? to
        };
        snapshot = await Task.Run(() => store.ReadDesktopSnapshotForFilter(filter));
        reminders = await Task.Run(() => store.ReadReminders());
        transactions = await Task.Run(() => store.ReadTransactions(filter, offset));
        overviewAnalytics = await Task.Run(() => store.ReadFlowAnalytics(filter));
        var averageInterval = overviewPeriod switch
        {
            OverviewPeriod.Day => AverageInterval.Day,
            OverviewPeriod.ThisWeek => AverageInterval.Week,
            OverviewPeriod.ThisYear => AverageInterval.Year,
            _ => AverageInterval.Month
        };
        var averageFilter = filter with
        {
            From = overviewPeriod == OverviewPeriod.Custom ? filter.From : null
        };
        averages = await Task.Run(() => store.ReadFlowAverages(averageFilter, averageInterval));
        analyticsFilter = filter;

        if (updateOverviewInPlace)
        {
            RenderOverview(snapshot!);
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
            Overview.IsEnabled = false;
            SetStatus("Working…");
        }
        try
        {
            await action();
            if (disableControls)
            {
                SetStatus("Saved locally");
            }
        }
        catch (Exception ex)
        {
            LogWorkflowFailure(ex, action.Method.Name);
            SetStatus(FriendlyError(ex));
            await ShowErrorDialog("Balancia", FriendlyError(ex));
        }
        finally
        {
            busy = false;
            if (disableControls)
            {
                Overview.IsEnabled = true;
            }

            if (pendingOverviewFilterRefresh)
            {
                pendingOverviewFilterRefresh = false;
                Dispatcher.UIThread.Post(() => _ = RequestOverviewFilterRefresh());
            }
        }
    }

    private void Render()
    {
        Overview.IsVisible = true;
        if (snapshot is not { } s)
        {
            Overview.Content = Text("The ledger could not be loaded. Check the message below and restart after resolving it.");
            return;
        }

        Overview.Content = OverviewRoot;
        LocalizeOverview();
        RenderAccountPanel(s);
        RenderCategoryPanel(s);
        RenderRemindersPanel();
        FillCategoriesPanel(CategoryBody, s);
        SyncOverviewFilterInputs();
        SyncOverviewFilterDates();
        renderedPeriodState = null;
        TransactionsList.ItemsSource = null;
        RenderOverview(s);
    }

    private void InitializeOverview()
    {
        SettingsButton.Click += async (_, _) => await OpenSettingsDialog();
        InitializeOverviewFilters();
        InitializeAccountPanel();
        InitializeCategoryPanel();
        InitializeTransactionsPanel();
        InitializeRemindersPanel();
        InitializeTrendPanel();
    }
}
