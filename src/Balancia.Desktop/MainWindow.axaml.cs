using Avalonia.Controls;
using System.Globalization;
using Avalonia.Input;
using Avalonia.Threading;
using Balancia.Core;
using Balancia.Storage;

using static Balancia.Desktop.FormControls;
using static Balancia.Desktop.Localization.UiText;

namespace Balancia.Desktop;

public partial class MainWindow : Window
{
    private LedgerStore store;
    private CloseTimeExporter closeExporter;
    internal string DatabasePath;
    private readonly SettingsStore settingsStore;
    private readonly bool separateDataFolder;
    private ApplicationSettings settings;
    internal string LanguageCode;
    private readonly OperationRunner operations;
    private readonly EditDialog dialogs;
    private readonly TimeProvider timeProvider;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(30) };
    private string? lastAccountId;

    internal string? BackupPath => settings.BackupPath;

    internal string? SnapshotPath => settings.SnapshotPath;

    internal bool IsBusy => operations.IsBusy;

    private DateOnly Today => DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);

    private void SetStatus(string text) => OverviewPane.ShowStatus(text);

    public MainWindow() : this(SeparateDataDirectory(Environment.GetCommandLineArgs()), ApplicationPaths.SettingsFile)
    {
    }

    /// <param name="separateDataDirectory">The <c>--data-dir</c> folder. When set, the user's settings are neither read nor written.</param>
    /// <param name="settingsFile">The application settings file; tests point it at a temporary folder.</param>
    /// <param name="timeProvider">The clock behind "today"; tests supply a controllable one. Defaults to the system clock.</param>
    /// <param name="keyStore">Where the remembered snapshot key lives; tests supply a fake. Defaults to Windows DPAPI.</param>
    internal MainWindow(string? separateDataDirectory, string settingsFile, TimeProvider? timeProvider = null, ISnapshotKeyStore? keyStore = null)
    {
        this.timeProvider = timeProvider ?? TimeProvider.System;
        operations = new OperationRunner(new OperationHost(this), () => RefreshCore(true));
        dialogs = new EditDialog(this, () => operations.RunAsync(Refresh), () => operations.RunAsync(Refresh, false));
        OpenFilePicker = PickOpenFile;
        SaveFilePicker = PickSaveFile;
        FolderPicker = PickFolderFromDialog;
        keys = new SnapshotKeyService(keyStore ?? new WindowsSnapshotKeyStore(), () => SnapshotKeyPath);
        InitializeComponent();
        AppVersionText.Text = typeof(MainWindow).Assembly.GetName().Version!.ToString(3);
        InitializeOverview();
        separateDataFolder = separateDataDirectory is not null;
        settingsStore = new SettingsStore(settingsFile);
        var savedSettings = settingsStore.Load();
        var directory = separateDataDirectory is not null
            ? Path.GetFullPath(separateDataDirectory)
            : Path.GetDirectoryName(SettingsStore.ResolveFullPath(savedSettings?.DatabasePath) ?? string.Empty) ?? ApplicationPaths.UserDirectory;
        LanguageCode = ResolveLanguage(savedSettings?.Language, CultureInfo.CurrentUICulture);
        SetLanguage(LanguageCode);
        DatabasePath = Path.Combine(directory, "balancia.db");
        settings = separateDataFolder
            ? new ApplicationSettings(DatabasePath, Language: savedSettings?.Language)
            : new ApplicationSettings(DatabasePath, SettingsStore.ResolveFullPath(savedSettings?.BackupPath),
                SettingsStore.ResolveFullPath(savedSettings?.SnapshotPath), savedSettings?.DefaultAccountId, savedSettings?.Language);
        store = new LedgerStore(DatabasePath);
        closeExporter = new CloseTimeExporter(store, this.timeProvider);
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
            await operations.RunAsync(() => LoadLedger(directory));
        };
        timer.Tick += async (_, _) => await RefreshIfDayChanged();
        Closing += (_, e) => e.Cancel = operations.IsBusy;
        Closed += (_, _) =>
        {
            timer.Stop();
            SaveWindowSettings();
            closeExporter.SaveBackup(BackupPath, keys.Key);
            closeExporter.SaveSnapshot(SnapshotPath, keys.Key);
            keys.Dispose();
        };
    }

    /// <summary>Re-renders the overview once the calendar day changes, so "today" and the month label never go stale.</summary>
    internal async Task RefreshIfDayChanged()
    {
        if (!operations.IsBusy && Today != OverviewPane.DisplayDate)
        {
            await operations.RunAsync(Refresh);
        }
    }

    private async Task LoadLedger(string directory)
    {
        await Task.Run(() =>
        {
            Directory.CreateDirectory(directory);
            Serilog.Log.Information("Initializing ledger, DatabasePath: '{DatabasePath}', SeparateDataFolder: '{SeparateDataFolder}'", DatabasePath, separateDataFolder);
            store.Initialize();
            closeExporter.MarkLoaded();
            Serilog.Log.Information("Ledger initialized");
        });
        await OverviewPane.StartAsync(store, Today);
        await Refresh();
        await InitializeEncryption();
    }

    private async Task HandleShortcut(KeyEventArgs e)
    {
        if (e.Handled || operations.IsBusy || OverviewPane.Data is null ||
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
            case Key.Delete when OverviewPane.TransactionsCard.SelectedEntry is { } entry:
                e.Handled = true;
                await RemoveTransaction(entry);
                break;
        }
    }

    private Task Refresh() => RefreshCore(false);

    private Task SelectFirst()
    {
        SetStatus("Select a row first.");
        return Task.CompletedTask;
    }

    private async Task RefreshCore(bool updateOverviewInPlace)
    {
        await OverviewPane.LoadAsync(store, Today);

        if (updateOverviewInPlace)
        {
            OverviewPane.RenderInPlace();
        }
        else
        {
            Render();
        }
    }

    /// <summary>What the window shows around an operation: the controls, the status line and the error dialog.</summary>
    private sealed class OperationHost(MainWindow window) : IOperationHost
    {
        public void WorkStarted()
        {
            window.Overview.IsEnabled = false;
            window.SetStatus("Working…");
        }

        public void WorkSucceeded()
        {
            // An action that reported its own outcome (export, import, restore) keeps that message.
            if (window.OverviewPane.StatusText == Get("Working…"))
            {
                window.SetStatus("Saved locally");
            }
        }

        public async Task WorkFailedAsync(Exception exception, Window? errorOwner)
        {
            window.SetStatus(FriendlyErrors.For(exception));
            await ErrorDialog.Show(errorOwner ?? window, "Balancia", FriendlyErrors.For(exception));
        }

        public void WorkFinished() => window.Overview.IsEnabled = true;

        public void Post(Action action) => Dispatcher.UIThread.Post(action);
    }

    private void Render()
    {
        Overview.IsVisible = true;
        if (OverviewPane.Data is null)
        {
            Overview.Content = Text("The ledger could not be loaded. Check the message below and restart after resolving it.");
            return;
        }

        Overview.Content = OverviewPane;
        OverviewPane.Render(settings.DefaultAccountId);
    }

    private void InitializeOverview()
    {
        OverviewPane.SettingsRequested += async () => await OpenSettingsDialog();
        OverviewPane.ReloadRequested += () => operations.RequestRefreshAsync();
        InitializeAccountPanel();
        InitializeCategoryPanel();
        InitializeTransactionsPanel();
        InitializeRemindersPanel();
    }
}
