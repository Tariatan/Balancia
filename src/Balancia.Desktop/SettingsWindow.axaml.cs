using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;

using static Balancia.Desktop.Localization.UiText;

namespace Balancia.Desktop;

public partial class SettingsWindow : Window
{
    private MainWindow owner = null!;

    public SettingsWindow()
    {
        InitializeComponent();
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

        databaseButton.Click += async (_, _) => await ChangeLocation(owner.ChangeDatabaseLocation);
        backupButton.Click += async (_, _) => await ChangeLocation(owner.ChooseBackupLocation);
        snapshotButton.Click += async (_, _) => await ChangeLocation(owner.ChooseSnapshotLocation);
        importCsvButton.Click += async (_, _) => await owner.ImportCsv();
        exportCsvButton.Click += async (_, _) => await owner.ExportCsv();
        restoreSnapshotButton.Click += async (_, _) => await owner.RestoreSnapshot();
        exportSnapshotButton.Click += async (_, _) => await owner.ExportSnapshot();
        closeButton.Click += (_, _) => Close();

        Localize();
    }

    private async Task ChangeLocation(Func<Task> change)
    {
        await change();
        RenderLocations();
    }

    private void Localize()
    {
        Title = Get("Settings");
        heading.Text = Get("Settings");
        languageLabel.Text = Get("Language");
        databaseButton.Content = Get("Database folder");
        backupButton.Content = Get("Backup folder");
        snapshotButton.Content = Get("Snapshot folder");
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
            languagePicker.SelectedItem = MainWindow.Languages.Single(option => option.Code == owner.languageCode);
            await owner.ShowErrorDialog("Balancia", MainWindow.FriendlyError(ex));
        }
    }
}
