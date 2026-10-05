using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;

using static Balancia.Desktop.Localization.UiText;

namespace Balancia.Desktop;

public partial class SettingsWindow : Window
{
    private MainWindow owner = null!;
    private bool operationInProgress;

    public SettingsWindow()
    {
        InitializeComponent();
        versionText.Text = $"Balancia {typeof(SettingsWindow).Assembly.GetName().Version!.ToString(3)}";
    }

    public static Task ShowFor(MainWindow owner)
    {
        var window = new SettingsWindow { owner = owner };
        window.Initialize();
        return window.ShowDialog(owner);
    }

    private void Initialize()
    {
        languagePicker.ItemsSource = MainWindow.Languages;
        languagePicker.SelectedItem = MainWindow.Languages.Single(option => option.Code == owner.languageCode);
        languagePicker.ItemTemplate = new FuncDataTemplate<MainWindow.LanguageOption>((option, _) =>
            // Virtualized presenters can request a template with no item while scrolling.
            option is null
                ? null
                : new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 9,
                    Children =
                    {
                        MainWindow.FlagIcon(option.Code),
                        new TextBlock { Text = option.NativeName, VerticalAlignment = VerticalAlignment.Center }
                    }
                });
        languagePicker.SelectionChanged += OnLanguageChanged;

        databaseButton.Click += async (_, _) => await Perform(owner.ChangeDatabaseLocation);
        backupButton.Click += async (_, _) => await Perform(() => owner.ChooseBackupLocation(this));
        snapshotButton.Click += async (_, _) => await Perform(() => owner.ChooseSnapshotLocation(this));
        encryptionButton.Click += async (_, _) => await Perform(async () =>
        {
            await owner.ConfigureEncryption(this);
        });
        importCsvButton.Click += async (_, _) => await Perform(owner.ImportCsv);
        exportCsvButton.Click += async (_, _) => await Perform(owner.ExportCsv);
        restoreSnapshotButton.Click += async (_, _) => await Perform(() => owner.RestoreSnapshot(this));
        exportSnapshotButton.Click += async (_, _) => await Perform(() => owner.ExportSnapshot(this));
        closeButton.Click += (_, _) => Close();
        Closing += (_, e) => e.Cancel = operationInProgress;

        Localize();
    }

    private async Task Perform(Func<Task> action)
    {
        if (operationInProgress)
        {
            return;
        }

        operationInProgress = true;
        ((Control)Content!).IsEnabled = false;
        try
        {
            await action();
            RenderLocations();
        }
        finally
        {
            operationInProgress = false;
            ((Control)Content!).IsEnabled = true;
        }
    }

    private void Localize()
    {
        Title = Get("Settings");
        heading.Text = Get("Settings");
        languageLabel.Text = Get("Language");
        databaseButton.Content = Get("Database folder");
        backupButton.Content = Get("Backup folder");
        snapshotButton.Content = Get("Snapshot folder");
        encryptionHeading.Text = Get("Snapshot encryption");
        dataTransferHeading.Text = Get("Data transfer");
        importCsvButton.Content = Get("Import CSV");
        exportCsvButton.Content = Get("Export CSV");
        restoreSnapshotButton.Content = Get("Restore snapshot");
        exportSnapshotButton.Content = Get("Export snapshot");
        closeButton.Content = Get("Close");
        RenderLocations();
    }

    private void RenderLocations()
    {
        databaseText.Text = owner.databasePath;
        backupText.Text = owner.backupPath ?? Get("Not configured");
        snapshotText.Text = owner.snapshotPath ?? Get("Not configured");
        encryptionStatus.Text = Get(owner.EncryptionConfigured
            ? "Encrypted snapshots and backups. Key remembered on this Windows account. Local databases and CSV stay unencrypted."
            : "Set a passphrase before exporting snapshots or enabling automatic backups.");
        encryptionButton.Content = Get(owner.EncryptionConfigured ? "Change passphrase" : "Set passphrase");
    }

    private async void OnLanguageChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (languagePicker.SelectedItem is not MainWindow.LanguageOption selected || selected.Code == owner.languageCode)
        {
            return;
        }

        try
        {
            await owner.ApplyLanguageChange(selected.Code);
            Localize();
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Language change failed, Language: '{Language}'", selected.Code);
            languagePicker.SelectedItem = MainWindow.Languages.Single(option => option.Code == owner.languageCode);
            await owner.ShowErrorDialog("Balancia", MainWindow.FriendlyError(ex));
        }
    }
}
