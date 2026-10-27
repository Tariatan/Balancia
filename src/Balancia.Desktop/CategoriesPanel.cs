using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Balancia.Core;

using static Balancia.Desktop.FormControls;
using static Balancia.Desktop.InputParsing;
using static Balancia.Desktop.Localization.UiText;

namespace Balancia.Desktop;

public partial class MainWindow
{
    private void InitializeCategoryPanel()
    {
        CategoriesCard.AddRequested += async () => await EditCategory(null);
        CategoriesCard.EditRequested += async category => await EditCategory(category);
        CategoriesCard.SelectAllRequested += async () => await ToggleAllCategoryFilters();
        CategoriesCard.ArchiveRequested += async () => await ArchiveSelectedCategory();
        CategoriesCard.RemoveRequested += async () => await DeleteSelectedCategory();
        CategoriesCard.FilterToggled += async (id, selected) => await ToggleCategoryFilter(id, selected);
    }

    private void RenderCategoryPanel(LedgerSnapshot ledgerSnapshot) =>
        CategoriesCard.Render(ledgerSnapshot.Categories, overviewState.IsCategorySelected);

    private async Task ToggleCategoryFilter(string id, bool selected)
    {
        if (updatingFilterControls)
        {
            return;
        }

        overviewState.ToggleCategory(id, selected, overview!.Snapshot.Categories);
        CategoriesCard.SyncFilterChecks();
        await operations.RequestRefreshAsync();
    }

    private async Task ToggleAllCategoryFilters()
    {
        if (updatingFilterControls || overview is null)
        {
            return;
        }

        overviewState.ToggleAllCategories(overview.Snapshot.Categories);
        CategoriesCard.SyncFilterChecks();
        await operations.RequestRefreshAsync();
    }

    private async Task ArchiveSelectedCategory()
    {
        if (CategoriesCard.Selected is not { } category)
        {
            return;
        }

        await EditDialog("Archive category",
            [
                Text(Format("Archive {0}?", category.Path)),
                Text("Subcategories will also be archived.")
            ],
            () => () => store.SaveCategory(category.Id, category.Name, category.ParentId, true),
            DialogOptions.Confirmation("Archive"));
    }

    private async Task DeleteSelectedCategory()
    {
        if (CategoriesCard.Selected is not { } category)
        {
            return;
        }

        await EditDialog("Delete category",
            [
                Text(Format("Delete {0}?", category.Path)),
                Text("Categories used by transactions or with subcategories must be archived instead.")
            ],
            () => () => store.DeleteCategory(category.Id),
            DialogOptions.Confirmation("Delete", () =>
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
            }));
    }

    private async Task EditCategory(Category? category)
    {
        var categoryArchived = category?.Archived ?? false;
        var name = Input(category?.Name ?? string.Empty);
        var options = new List<Choice<string?>> { new(null, Get("No parent (top-level)")) };
        options.AddRange(overview!.Snapshot.Categories
            .Where(c => c.ParentId is null && !c.Archived && c.Id != category?.Id)
            .Select(c => new Choice<string?>(c.Id, c.Path)));

        if (category?.ParentId is { } current && options.All(c => c.Value != current))
        {
            var archivedParent = overview!.Snapshot.Categories.Single(c => c.Id == current);
            options.Add(new Choice<string?>(current, ArchivedLabel.Of(archivedParent.Path, archivedParent.Archived)));
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
                var newName = name.Text ?? string.Empty;
                var newParentId = ((Choice<string?>)parent.SelectedItem!).Value;
                return () => store.SaveCategory(category?.Id, newName, newParentId, categoryArchived);
            },
            new DialogOptions(Validate: () =>
            {
                var trimmedName = (name.Text ?? string.Empty).Trim();
                if (trimmedName.Length == 0 || trimmedName.Contains('/'))
                {
                    return "Enter a category name without '/'; choose its parent separately.";
                }

                var newParentId = ((Choice<string?>)parent.SelectedItem!).Value;
                if (newParentId is null)
                {
                    return null;
                }

                var parentCategory = overview!.Snapshot.Categories.SingleOrDefault(c => c.Id == newParentId);
                if (parentCategory is { Archived: true } && (!categoryArchived || category!.ParentId != newParentId))
                {
                    return "Restore the parent category before adding or restoring a child.";
                }

                if (category is not null && overview!.Snapshot.Categories.Any(c => c.ParentId == category.Id))
                {
                    return "A category with subcategories must remain top-level.";
                }

                return null;
            }));
    }
}
