using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Balancia.Storage;

using static Balancia.Desktop.Localization.UiText;

namespace Balancia.Desktop;

public partial class MainWindow
{
    private async Task OpenSettingsDialog()
    {
        var dialog = new Window
        {
            Title = Get("Settings"),
            Icon = Icon,
            ShowInTaskbar = false,
            Width = 600,
            Height = 470,
            MinWidth = 600,
            MinHeight = 470,
            MaxWidth = 600,
            MaxHeight = 470,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        var body = new StackPanel
        {
            Spacing = 12,
            Margin = new Thickness(24)
        };
        body.Children.Add(new TextBlock
        {
            Text = Get("Settings"),
            FontSize = 24,
            FontWeight = FontWeight.SemiBold
        });
        var languageLabel = Text(Get("Language"));
        languageLabel.VerticalAlignment = VerticalAlignment.Center;
        var languageRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Children =
            {
                languageLabel,
                LanguagePicker(dialog),
            },
        };
        body.Children.Add(languageRow);

        var locations = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto"),
            RowSpacing = 7,
            ColumnSpacing = 12
        };
        AddRow(locations, ActionButton("Database folder", ChangeDatabaseLocation), 0);
        AddRow(locations, ActionButton("Backup folder", ChooseBackupLocation), 1);
        AddRow(locations, ActionButton("Snapshot folder", ChooseSnapshotLocation), 2);
        var databaseText = Text(databasePath);
        var backupText = Text(backupPath ?? Get("Not configured"));
        var snapshotText = Text(snapshotPath ?? Get("Not configured"));
        foreach (var (text, row) in new[] { (databaseText, 0), (backupText, 1), (snapshotText, 2) })
        {
            text.VerticalAlignment = VerticalAlignment.Center;
            AddColumn(locations, text, 1);
            Grid.SetRow(text, row);
        }
        body.Children.Add(locations);
        body.Children.Add(new Separator { Margin = new Thickness(0, 4) });
        body.Children.Add(new TextBlock
        {
            Text = Get("Data transfer"),
            FontWeight = FontWeight.SemiBold
        });
        var dataTransferActions = new StackPanel
        {
            Spacing = 0,
            Children =
            {
                Row(
                    ActionButton("Import CSV", ImportCsv),
                    ActionButton("Export CSV", ExportCsv)),
                Row(
                    ActionButton("Restore snapshot", RestoreSnapshot),
                    ActionButton("Export snapshot", ExportSnapshot)),
            },
        };
        body.Children.Add(dataTransferActions);
        var close = new Button
        {
            Content = Get("Close"),
            IsCancel = true,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        body.Children.Add(close);

        dialog.Content = new ScrollViewer { Content = body };
        close.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(this);
    }

    private async Task<string?> PickFolder(string title)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = Get(title),
            AllowMultiple = false
        });
        var directory = folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
        return directory is null ? null : Path.GetFullPath(directory);
    }

    private async Task ChangeDatabaseLocation()
    {
        var directory = await PickFolder("Choose Balancia database folder");
        if (directory is null)
        {
            return;
        }

        var dbPath = Path.Combine(directory, "balancia.db");
        if (string.Equals(dbPath, this.databasePath, StringComparison.OrdinalIgnoreCase))
        {
            SetStatus("This database folder is already active.");
            return;
        }

        await Run(async () =>
        {
            var replacement = new LedgerStore(dbPath);
            await Task.Run(replacement.Initialize);
            await Task.Run(() => SaveApplicationSettings(dbPath));
            store = replacement;
            this.databasePath = dbPath;
            windowSettingsPath = Path.Combine(Path.GetDirectoryName(dbPath)!, "window.json");
            await Refresh();
        });
    }

    private async Task ChooseBackupLocation()
    {
        var selected = await PickFolder("Choose backup folder");
        if (selected is null)
        {
            return;
        }

        await Run(async () =>
        {
            await Task.Run(() => SaveApplicationSettings(databasePath, selected));
            backupPath = selected;
            await Refresh();
        });
    }

    private async Task ChooseSnapshotLocation()
    {
        var selected = await PickFolder("Choose snapshot folder");
        if (selected is null)
        {
            return;
        }

        await Run(async () =>
        {
            await Task.Run(() => SaveApplicationSettings(databasePath, backupPath, selected));
            snapshotPath = selected;
            await Refresh();
        });
    }
}
