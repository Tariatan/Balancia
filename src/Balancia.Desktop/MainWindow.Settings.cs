using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Balancia.Core;
using Balancia.Storage;

namespace Balancia.Desktop;

public partial class MainWindow
{
    private Border CategoriesPanel(LedgerSnapshot ledgerSnapshot)
    {
        var body = new StackPanel
        {
            Spacing = 1
        };
        var header = SectionHeader("Top expenditures", "View all →", () => Navigate("Category"));
        overviewCategoryHeading = (TextBlock)header.Children[0];
        body.Children.Add(header);
        var rows = new StackPanel { Spacing = 1 };
        body.Children.Add(rows);
        overviewCategoryBody = rows;
        FillCategoriesPanel(rows, ledgerSnapshot);
        return Panel(body);
    }

    private void FillCategoriesPanel(StackPanel body, LedgerSnapshot ledgerSnapshot)
    {
        body.Children.Clear();
        renderedOverviewCategories = [.. ledgerSnapshot.LargestCategories];
        renderedOverviewCategoryId = overviewFilter.CategoryId;
        var selectedCategory = ledgerSnapshot.Categories.FirstOrDefault(c => c.Id == overviewFilter.CategoryId);
        var hasSubcategories = selectedCategory is not null &&
            ledgerSnapshot.Categories.Any(c => c.ParentId == selectedCategory.Id);
        var title = hasSubcategories
            ? $"Top expenditures · {selectedCategory!.Name}"
            : "Top expenditures";
        overviewCategoryHeading!.Text = title;

        if (ledgerSnapshot.LargestCategories.Count == 0)
        {
            body.Children.Add(QuietText("No matching expenses.", 12));
        }

        var maximum = ledgerSnapshot.LargestCategories.Count > 0 ? ledgerSnapshot.LargestCategories[0].Amount.Centimes : 1;

        foreach (var category in ledgerSnapshot.LargestCategories)
        {
            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("75,*,80"),
                MinHeight = 20
            };

            row.Children.Add(new TextBlock
            {
                Text = category.Name,
                FontSize = 11,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            });

            var bar = new ProgressBar
            {
                Minimum = 0,
                Maximum = maximum,
                Value = category.Amount.Centimes,
                Height = 7,
                Foreground = Brush.Parse("#3989A7"),
                Background = Brush.Parse("#E7F2F6"),
                VerticalAlignment = VerticalAlignment.Center
            };

            AddColumn(row, bar, 1);
            var value = new TextBlock
            {
                Text = AmountText(category.Amount),
                FontSize = 11,
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeight.SemiBold
            };

            AddColumn(row, value, 2);
            body.Children.Add(row);
        }
    }

    private void RenderSettings(LedgerSnapshot ledgerSnapshot)
    {
        var layout = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*"),
            RowSpacing = 11
        };
        AddRow(layout, OverviewFilterPanel(), 0);
        var instruction = Text("Choose an optional parent for a subcategory. Archiving a parent also archives its children.");
        AddRow(layout, instruction, 1);

        var categories = new ListBox
        {
            ItemsSource = ledgerSnapshot.Categories.Select(c => new Choice<Category>(c, c.Path + (c.Archived ? " (archived)" : ""))).ToArray(),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0)
        };

        var actions = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto")
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 3
        };

        buttons.Children.Add(IconButton("+", "Add category", () => EditCategory(null)));
        buttons.Children.Add(IconButton("▣", "Archive selected category", () => ArchiveSelectedCategory(categories)));
        buttons.Children.Add(IconButton("🗑", "Delete selected category", () => DeleteSelectedCategory(categories)));
        AddColumn(actions, buttons, 1);
        AddRow(layout, actions, 2);
        categories.DoubleTapped += async (_, _) =>
        {
            if (categories.SelectedItem is Choice<Category> choice)
            {
                await EditCategory(choice.Value);
            }
        };
        AddRow(layout, Panel(categories), 3);
        ResponsiveBody.Content = layout;
    }

    private async Task OpenSettingsDialog()
    {
        var body = new StackPanel
        {
            Spacing = 12,
            Margin = new Thickness(24)
        };
        body.Children.Add(new TextBlock
        {
            Text = "Settings",
            FontSize = 24,
            FontWeight = FontWeight.SemiBold
        });

        var locations = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto"),
            RowSpacing = 7,
            ColumnSpacing = 12
        };
        AddRow(locations, ActionButton("Change database location", ChangeDatabaseLocation), 0);
        AddRow(locations, ActionButton("Choose backup folder", ChooseBackupLocation), 1);
        AddRow(locations, ActionButton("Choose snapshot folder", ChooseSnapshotLocation), 2);
        var databaseText = Text(databasePath);
        var backupText = Text(backupPath ?? "Not configured");
        var snapshotText = Text(snapshotPath ?? "Not configured");
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
            Text = "Data transfer",
            FontWeight = FontWeight.SemiBold
        });
        body.Children.Add(Row(
            ActionButton("Import CSV", ImportCsv),
            ActionButton("Export CSV", ExportCsv),
            ActionButton("Export snapshot", ExportSnapshot),
            ActionButton("Restore snapshot", RestoreSnapshot)));
        var close = new Button
        {
            Content = "Close",
            IsCancel = true,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        body.Children.Add(close);

        var dialog = new Window
        {
            Title = "Settings",
            Icon = Icon,
            ShowInTaskbar = false,
            Width = 600,
            Height = 400,
            MinWidth = 600,
            MinHeight = 400,
            MaxWidth = 600,
            MaxHeight = 400,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new ScrollViewer { Content = body }
        };
        close.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(this);
    }

    private async Task<string?> PickFolder(string title)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
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
            Status.Text = "This database folder is already active.";
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

    private async Task ArchiveSelectedCategory(ListBox categories)
    {
        if (categories.SelectedItem is not Choice<Category> choice)
        {
            return;
        }

        var category = choice.Value;

        await EditDialog("Archive category",
            [
                Text($"Archive {category.Path}?"),
                Text("Subcategories will also be archived.")
            ],
            () => () => store.SaveCategory(category.Id, category.Name, category.ParentId, true),
            "Archive");
    }

    private async Task DeleteSelectedCategory(ListBox categories)
    {
        if (categories.SelectedItem is not Choice<Category> choice)
        {
            return;
        }

        var category = choice.Value;

        await EditDialog("Delete category",
            [
                Text($"Delete {category.Path}?"),
                Text("Categories used by transactions or with subcategories must be archived instead.")
            ],
            () => () => store.DeleteCategory(category.Id),
            "Delete");
    }

    private async Task EditCategory(Category? category)
    {
        var name = Input(category?.Name ?? "");
        var options = new List<Choice<string?>> { new(null, "No parent (top-level)") };
        options.AddRange(snapshot!.Categories.
            Where(c => c.ParentId is null && !c.Archived && c.Id != category?.Id).
            Select(c => new Choice<string?>(c.Id, c.Path)));

        if (category?.ParentId is { } current && options.All(c => c.Value != current))
        {
            var archivedParent = snapshot.Categories.Single(c => c.Id == current);
            options.Add(new Choice<string?>(current, archivedParent.Path + (archivedParent.Archived ? " (archived)" : "")));
        }

        var parent = new ComboBox
        {
            ItemsSource = options,
            SelectedItem = options.FirstOrDefault(c => c.Value == category?.ParentId) ?? options[0],
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        await EditDialog(category is null ? "Add category" : "Edit category",
            [
                Field("Name", name),
                Field("Parent category", parent)
            ],
            () =>
            {
                var newName = name.Text ?? "";
                var newParentId = ((Choice<string?>)parent.SelectedItem!).Value;
                var archived = category?.Archived ?? false;
                return () => store.SaveCategory(category?.Id, newName, newParentId, archived);
            });
    }
}
