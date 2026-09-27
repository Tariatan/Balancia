using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Balancia.Core;

using static Balancia.Desktop.Localization.UiText;

namespace Balancia.Desktop;

public partial class MainWindow
{
    private readonly Dictionary<string, CheckBox> categoryFilterChecks = [];

    private bool IsCategorySelected(string id) =>
        overviewFilter.CategoryId == id || overviewFilter.CategoryIds?.Contains(id) == true;

    private void SyncCategoryFilterChecks()
    {
        var wasUpdating = updatingFilterControls;
        updatingFilterControls = true;
        try
        {
            foreach (var (id, check) in categoryFilterChecks)
            {
                check.IsChecked = IsCategorySelected(id);
            }
        }
        finally
        {
            updatingFilterControls = wasUpdating;
        }
    }

    private async Task ToggleCategoryFilter(string id, bool selected)
    {
        if (updatingFilterControls)
        {
            return;
        }

        var ids = new HashSet<string>(overviewFilter.CategoryIds ?? []);
        if (overviewFilter.CategoryId is { } single)
        {
            ids.Add(single);
        }

        if (selected)
        {
            ids.Add(id);
        }
        else
        {
            ids.Remove(id);
        }

        overviewFilter = overviewFilter with
        {
            CategoryId = ids.Count == 1 ? ids.Single() : null,
            CategoryIds = ids.Count > 1 ? ids.Order().ToArray() : null,
        };
        overviewOffset = 0;
        SyncCategoryFilterChecks();
        await RequestOverviewFilterRefresh();
    }

    private Border CategoryManagementPanel(LedgerSnapshot ledgerSnapshot)
    {
        categoryFilterChecks.Clear();

        var categories = new ListBox
        {
            Classes = { "compact-list" },
            ItemsSource = ledgerSnapshot.Categories
                .Select(category => new Choice<Category>(category,
                    category.Path + (category.Archived ? Get(" (archived)") : string.Empty)))
                .ToArray(),
            ItemTemplate = new FuncDataTemplate<Choice<Category>>((choice, _) =>
            {
                // Virtualized presenters can request a template with no item while scrolling.
                if (choice is null)
                {
                    return null;
                }

                var category = choice.Value;
                var check = new CheckBox
                {
                    Classes = { "compact-filter-checkbox" },
                    IsChecked = IsCategorySelected(category.Id),
                };
                AutomationProperties.SetName(check, category.Path);
                ToolTip.SetTip(check, category.Path);
                categoryFilterChecks[category.Id] = check;
                check.IsCheckedChanged += async (_, _) =>
                {
                    if (!updatingFilterControls && check.FindAncestorOfType<ListBox>() is { } list)
                    {
                        list.SelectedItem = choice;
                    }

                    await ToggleCategoryFilter(category.Id, check.IsChecked == true);
                };
                var row = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                    ColumnSpacing = 7,
                    Margin = new Thickness(category.ParentId is null ? 0 : 15, 0, 0, 0),
                };
                row.Children.Add(check);
                var name = new TextBlock
                {
                    Text = category.Name + (category.Archived ? Get(" (archived)") : string.Empty),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                AddColumn(row, name, 1);
                return row;
            }),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0)
        };
        var categoryBody = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            RowSpacing = 6
        };
        var categoryHeader = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto")
        };
        categoryHeader.Children.Add(Heading("Categories", 11));
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 3
        };
        buttons.Children.Add(IconButton("+", "Add category", () => EditCategory(null)));
        buttons.Children.Add(IconButton("▣", "Archive selected category", () => ArchiveSelectedCategory(categories)));
        buttons.Children.Add(IconButton("🗑", "Delete selected category", () => DeleteSelectedCategory(categories)));
        AddColumn(categoryHeader, buttons, 1);
        AddRow(categoryBody, categoryHeader, 0);
        AddRow(categoryBody, categories, 1);
        categories.DoubleTapped += async (_, _) =>
        {
            if (categories.SelectedItem is Choice<Category> choice)
            {
                await EditCategory(choice.Value);
            }
        };
        return Panel(categoryBody);
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
                Text(Format("Archive {0}?", category.Path)),
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
                Text(Format("Delete {0}?", category.Path)),
                Text("Categories used by transactions or with subcategories must be archived instead.")
            ],
            () => () => store.DeleteCategory(category.Id),
            "Delete");
    }

    private async Task EditCategory(Category? category)
    {
        var name = Input(category?.Name ?? "");
        var options = new List<Choice<string?>> { new(null, Get("No parent (top-level)")) };
        options.AddRange(snapshot!.Categories.
            Where(c => c.ParentId is null && !c.Archived && c.Id != category?.Id).
            Select(c => new Choice<string?>(c.Id, c.Path)));

        if (category?.ParentId is { } current && options.All(c => c.Value != current))
        {
            var archivedParent = snapshot.Categories.Single(c => c.Id == current);
            options.Add(new Choice<string?>(current, archivedParent.Path + (archivedParent.Archived ? Get(" (archived)") : "")));
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
