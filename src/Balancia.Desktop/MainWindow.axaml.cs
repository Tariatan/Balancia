using Avalonia.Controls;
using System.Globalization;
using Avalonia.Input;
using Avalonia.Threading;
using Balancia.Core;
using Balancia.Storage;

using static Balancia.Desktop.Localization.UiText;

namespace Balancia.Desktop;

public partial class MainWindow : Window
{
    private LedgerStore store;
    internal string DatabasePath;
    private readonly string applicationSettingsPath;
    private readonly bool separateDataFolder;
    internal string? BackupPath;
    internal string? SnapshotPath;
    private string? defaultAccountId;
    internal string LanguageCode;
    private string? languagePreference;
    private LedgerSnapshot? snapshot;
    private FlowAverages averages = new(Money.Zero, Money.Zero);
    private IReadOnlyList<Reminder> reminders = [];
    private bool busy;
    private readonly TimeProvider timeProvider;
    private DateOnly displayDate;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(30) };
    private string? lastAccountId;

    private DateOnly Today => DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);

    private void SetStatus(string text) => OverviewStatus.Text = Get(text);

    public MainWindow() : this(SeparateDataDirectory(Environment.GetCommandLineArgs()), ApplicationPaths.SettingsFile)
    {
    }

    /// <param name="separateDataDirectory">The <c>--data-dir</c> folder. When set, the user's settings are neither read nor written.</param>
    /// <param name="settingsFile">The application settings file; tests point it at a temporary folder.</param>
    /// <param name="timeProvider">The clock behind "today"; tests supply a controllable one. Defaults to the system clock.</param>
    internal MainWindow(string? separateDataDirectory, string settingsFile, TimeProvider? timeProvider = null)
    {
        this.timeProvider = timeProvider ?? TimeProvider.System;
        displayDate = Today;
        InitializeComponent();
        AppVersionText.Text = typeof(MainWindow).Assembly.GetName().Version!.ToString(3);
        InitializeOverview();
        separateDataFolder = separateDataDirectory is not null;
        applicationSettingsPath = settingsFile;
        var savedSettings = LoadApplicationSettings(applicationSettingsPath);
        var directory = separateDataDirectory is not null
            ? Path.GetFullPath(separateDataDirectory)
            : Path.GetDirectoryName(ResolveFullPath(savedSettings?.DatabasePath) ?? string.Empty) ?? ApplicationPaths.UserDirectory;
        languagePreference = savedSettings?.Language;
        LanguageCode = ResolveLanguage(savedSettings?.Language, CultureInfo.CurrentUICulture);
        SetLanguage(LanguageCode);
        BackupPath = separateDataFolder ? null : ResolveFullPath(savedSettings?.BackupPath);
        SnapshotPath = separateDataFolder ? null : ResolveFullPath(savedSettings?.SnapshotPath);
        defaultAccountId = separateDataFolder ? null : savedSettings?.DefaultAccountId;
        DatabasePath = Path.Combine(directory, "balancia.db");
        store = new LedgerStore(DatabasePath);
        LoadWindowSettings(savedSettings);
        if (separateDataFolder)
        {
            Title = $"Balancia — {Get("Separate data folder")}";
        }

        WireLifecycle(directory);
        KeyDown += async (_, e) => await HandleShortcut(e);
    }

    private static string? SeparateDataDirectory(string[] args)
    {
        var index = Array.IndexOf(args, "--data-dir");
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private void WireLifecycle(string directory)
    {
        PositionChanged += (_, _) => windowPositionForPersistence = Position;
        Opened += async (_, _) =>
        {
            ApplyLoadedWindowPosition();
            timer.Start();
            await Run(() => LoadLedger(directory));
        };
        timer.Tick += async (_, _) => await RefreshIfDayChanged();
        Closing += (_, e) => e.Cancel = busy;
        Closed += (_, _) =>
        {
            timer.Stop();
            SaveWindowSettings();
            SaveBackupOnClose();
            SaveSnapshotOnClose();
            snapshotKey?.Dispose();
        };
    }

    /// <summary>Re-renders the overview once the calendar day changes, so "today" and the month label never go stale.</summary>
    internal async Task RefreshIfDayChanged()
    {
        if (!busy && Today != displayDate)
        {
            await Run(Refresh);
        }
    }

    private async Task LoadLedger(string directory)
    {
        await Task.Run(() =>
        {
            Directory.CreateDirectory(directory);
            Serilog.Log.Information("Initializing ledger, DatabasePath: '{DatabasePath}', SeparateDataFolder: '{SeparateDataFolder}'", DatabasePath, separateDataFolder);
            store.Initialize();
            Serilog.Log.Information("Ledger initialized");
        });
        var monthStart = new DateOnly(displayDate.Year, displayDate.Month, 1);
        var monthFilter = new TransactionsFilter
        {
            From = monthStart,
            To = monthStart.AddMonths(1).AddDays(-1),
        };
        var monthTransactions = await Task.Run(() => store.ReadTransactions(monthFilter, pageSize: 1));
        if (monthTransactions.TotalCount == 0)
        {
            overviewPeriod = OverviewPeriod.Last30Days;
        }

        await Refresh();
        await InitializeEncryption();
    }

    private async Task HandleShortcut(KeyEventArgs e)
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
            case Key.Delete when TransactionsList.SelectedItem is TransactionItem item:
                e.Handled = true;
                await RemoveTransaction(item.Hit.Entry);
                break;
        }
    }

    private Task Refresh() => RefreshCore(false);

    private async Task RefreshCore(bool updateOverviewInPlace)
    {
        displayDate = Today;
        var (from, to) = OverviewRange();
        var dateFilter = filter with
        {
            From = filter.From ?? from,
            To = filter.To ?? to
        };
        var pageOffset = offset;
        var averageInterval = overviewPeriod switch
        {
            OverviewPeriod.Day => AverageInterval.Day,
            OverviewPeriod.ThisWeek => AverageInterval.Week,
            OverviewPeriod.ThisYear => AverageInterval.Year,
            _ => AverageInterval.Month
        };
        var averageFilter = dateFilter with
        {
            From = overviewPeriod == OverviewPeriod.Custom ? dateFilter.From : null
        };
        // Each store call opens its own connection, so the reads can run concurrently.
        var snapshotRead = Task.Run(() => store.ReadDesktopSnapshotForFilter(dateFilter));
        var remindersRead = Task.Run(() => store.ReadReminders());
        var transactionsRead = Task.Run(() => store.ReadTransactions(dateFilter, pageOffset));
        var analyticsRead = Task.Run(() => store.ReadFlowAnalytics(dateFilter));
        var averagesRead = Task.Run(() => store.ReadFlowAverages(averageFilter, averageInterval));
        await Task.WhenAll(snapshotRead, remindersRead, transactionsRead, analyticsRead, averagesRead);
        snapshot = snapshotRead.Result;
        reminders = remindersRead.Result;
        transactions = transactionsRead.Result;
        overviewAnalytics = analyticsRead.Result;
        averages = averagesRead.Result;
        analyticsFilter = dateFilter;

        if (updateOverviewInPlace)
        {
            RenderOverview(snapshot!);
        }
        else
        {
            Render();
        }
    }

    private async Task Run(Func<Task> action, bool disableControls = true, Window? errorOwner = null, string? operationName = null)
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
            Serilog.Log.Error(ex, "Desktop operation failed, Operation: '{Operation}'", operationName ?? action.Method.Name);
            SetStatus(FriendlyError(ex));
            await ErrorDialog.Show(errorOwner ?? this, "Balancia", FriendlyError(ex));
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
