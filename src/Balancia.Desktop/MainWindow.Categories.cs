using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Balancia.Core;

using static Balancia.Desktop.Localization.UiText;

namespace Balancia.Desktop;

public partial class MainWindow
{
    private readonly Dictionary<string, CheckBox> categoryFilterChecks = [];

    private bool IsCategorySelected(string id) =>
        filter.CategoryId == id || filter.CategoryIds?.Contains(id) == true;

    private void InitializeCategoryPanel()
    {
        CategoriesList.ItemTemplate = new FuncDataTemplate<Choice<Category>>((choice, _) =>
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
        });
        CategoriesList.DoubleTapped += async (_, _) =>
        {
            if (CategoriesList.SelectedItem is Choice<Category> choice)
            {
                await EditCategory(choice.Value);
            }
        };
        CategoryAddButton.Click += async (_, _) => await EditCategory(null);
        CategoryArchiveButton.Click += async (_, _) => await ArchiveSelectedCategory(CategoriesList);
        CategoryRemoveButton.Click += async (_, _) => await DeleteSelectedCategory(CategoriesList);
    }

    private void RenderCategoryPanel(LedgerSnapshot ledgerSnapshot)
    {
        CategoriesHeading.Text = Get("Categories");
        ToolTip.SetTip(CategoryAddButton, Get("Add category"));
        ToolTip.SetTip(CategoryArchiveButton, Get("Archive selected category"));
        ToolTip.SetTip(CategoryRemoveButton, Get("Delete selected category"));

        categoryFilterChecks.Clear();
        CategoriesList.ItemsSource = ledgerSnapshot.Categories
            .Select(category => new Choice<Category>(category,
                category.Path + (category.Archived ? Get(" (archived)") : string.Empty)))
            .ToArray();
    }

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

        var ids = new HashSet<string>(filter.CategoryIds ?? []);
        if (filter.CategoryId is { } single)
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

        filter = filter with
        {
            CategoryId = ids.Count == 1 ? ids.Single() : null,
            CategoryIds = ids.Count > 1 ? ids.Order().ToArray() : null,
        };
        offset = 0;
        SyncCategoryFilterChecks();
        await RequestOverviewFilterRefresh();
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
            "Delete",
            validate: () =>
            {
                if (store.CategoryHasSubcategories(category.Id))
                {
                    return "A category with subcategories cannot be deleted. Archive it instead.";
                }

                if (store.CategoryIsUsedByTransactions(category.Id))
                {
                    return "A category used by transactions cannot be deleted. Archive it instead.";
                }

                return null;
            });
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
            },
            validate: () =>
            {
                var trimmedName = (name.Text ?? "").Trim();
                if (trimmedName.Length == 0 || trimmedName.Contains('/'))
                {
                    return "Enter a category name without '/'; choose its parent separately.";
                }

                var newParentId = ((Choice<string?>)parent.SelectedItem!).Value;
                if (newParentId is null)
                {
                    return null;
                }

                var parentCategory = snapshot!.Categories.SingleOrDefault(c => c.Id == newParentId);
                var categoryArchived = category?.Archived ?? false;
                if (parentCategory is { Archived: true } && (!categoryArchived || category!.ParentId != newParentId))
                {
                    return "Restore the parent category before adding or restoring a child.";
                }

                if (category is not null && snapshot.Categories.Any(c => c.ParentId == category.Id))
                {
                    return "A category with subcategories must remain top-level.";
                }

                return null;
            });
    }
}
