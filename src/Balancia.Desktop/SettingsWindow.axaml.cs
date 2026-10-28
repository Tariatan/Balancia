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
        VersionText.Text = $"Balancia {typeof(SettingsWindow).Assembly.GetName().Version!.ToString(3)}";
    }

    public static Task ShowFor(MainWindow owner)
    {
        var window = new SettingsWindow { owner = owner };
        window.Initialize();
        return window.ShowDialog(owner);
    }

    private void Initialize()
    {
        LanguagePicker.ItemsSource = MainWindow.Languages;
        SelectCurrentLanguage();
        LanguagePicker.ItemTemplate = new FuncDataTemplate<MainWindow.LanguageOption>((option, _) =>
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
                        new TextBlock { Text = option.NativeName, VerticalAlignment = VerticalAlignment.Center },
                    },
                });
        LanguagePicker.SelectionChanged += OnLanguageChanged;

        ThemePicker.ItemsSource = owner.Themes;
        SelectCurrentTheme();
        ThemePicker.SelectionChanged += OnThemeChanged;

        DatabaseButton.Click += async (_, _) => await Perform(owner.ChangeDatabaseLocation);
        BackupButton.Click += async (_, _) => await Perform(() => owner.ChooseBackupLocation(this));
        SnapshotButton.Click += async (_, _) => await Perform(() => owner.ChooseSnapshotLocation(this));
        EncryptionButton.Click += async (_, _) => await Perform(() => owner.ConfigureEncryption(this));
        ImportCsvButton.Click += async (_, _) => await Perform(owner.ImportCsv);
        ExportCsvButton.Click += async (_, _) => await Perform(owner.ExportCsv);
        RestoreSnapshotButton.Click += async (_, _) => await Perform(() => owner.RestoreSnapshot(this));
        ExportSnapshotButton.Click += async (_, _) => await Perform(() => owner.ExportSnapshot(this));
        CloseButton.Click += (_, _) => Close();
        Closing += (_, e) => e.Cancel = operationInProgress;

        Localize();
    }

    private void SelectCurrentLanguage() =>
        LanguagePicker.SelectedItem = MainWindow.Languages.Single(option => option.Code == owner.LanguageCode);

    private void SelectCurrentTheme() =>
        ThemePicker.SelectedItem = owner.Themes.Single(theme => theme.Name == owner.ThemeName);

    private async Task Perform(Func<Task> action)
    {
        if (operationInProgress)
        {
            return;
        }

        operationInProgress = true;
        ContentRoot.IsEnabled = false;
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Settings operation failed");
            await ErrorDialog.Show(owner, "Balancia", FriendlyErrors.For(ex));
        }
        finally
        {
            RenderLocations();
            operationInProgress = false;
            ContentRoot.IsEnabled = true;
        }
    }

    private void Localize()
    {
        var settings = Get("Settings");
        Title = settings;
        Heading.Text = settings;
        LanguageLabel.Text = Get("Language");
        ThemeLabel.Text = Get("Theme");
        DatabaseButton.Content = Get("Database folder");
        BackupButton.Content = Get("Backup folder");
        SnapshotButton.Content = Get("Snapshot folder");
        EncryptionHeading.Text = Get("Snapshot encryption");
        DataTransferHeading.Text = Get("Data transfer");
        ImportCsvButton.Content = Get("Import CSV");
        ExportCsvButton.Content = Get("Export CSV");
        RestoreSnapshotButton.Content = Get("Restore snapshot");
        ExportSnapshotButton.Content = Get("Export snapshot");
        CloseButton.Content = Get("Close");
        RenderLocations();
    }

    private void RenderLocations()
    {
        var notConfigured = Get("Not configured");
        var encryptionConfigured = owner.EncryptionConfigured;

        DatabaseText.Text = owner.DatabasePath;
        BackupText.Text = owner.BackupPath ?? notConfigured;
        SnapshotText.Text = owner.SnapshotPath ?? notConfigured;
        EncryptionStatus.Text = Get(encryptionConfigured
            ? "Encrypted snapshots and backups. Key remembered on this Windows account. Local databases and CSV stay unencrypted."
            : "Set a passphrase before exporting snapshots or enabling automatic backups.");
        EncryptionButton.Content = Get(encryptionConfigured ? "Change passphrase" : "Set passphrase");
    }

    private async void OnThemeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ThemePicker.SelectedItem is not ColorTheme selected || selected.Name == owner.ThemeName)
        {
            return;
        }

        try
        {
            owner.ApplyThemeChange(selected.Name);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Theme change failed, Theme: '{Theme}'", selected.Name);
            SelectCurrentTheme();
            await ErrorDialog.Show(owner, "Balancia", FriendlyErrors.For(ex));
        }
    }

    private async void OnLanguageChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (LanguagePicker.SelectedItem is not MainWindow.LanguageOption selected || selected.Code == owner.LanguageCode)
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
            SelectCurrentLanguage();
            await ErrorDialog.Show(owner, "Balancia", FriendlyErrors.For(ex));
        }
    }
}
