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
        OverviewPane.CategoriesCard.AddRequested += async () => await EditCategory(null);
        OverviewPane.CategoriesCard.EditRequested += async category => await EditCategory(category);
        OverviewPane.CategoriesCard.SelectAllRequested += async () => await ToggleAllCategoryFilters();
        OverviewPane.CategoriesCard.ArchiveRequested += async () => await ArchiveSelectedCategory();
        OverviewPane.CategoriesCard.RemoveRequested += async () => await DeleteSelectedCategory();
        OverviewPane.CategoriesCard.FilterToggled += async (id, selected) => await ToggleCategoryFilter(id, selected);
    }

    private Task ToggleCategoryFilter(string id, bool selected) =>
        ApplyOverviewChange(OverviewPane.State.ToggleCategory(id, selected, OverviewPane.Data!.Snapshot.Categories));

    private Task ToggleAllCategoryFilters() =>
        OverviewPane.Data is null ? Task.CompletedTask : ApplyOverviewChange(OverviewPane.State.ToggleAllCategories(OverviewPane.Data.Snapshot.Categories));

    private async Task ArchiveSelectedCategory()
    {
        if (OverviewPane.CategoriesCard.Selected is not { } category)
        {
            return;
        }

        await dialogs.ShowAsync("Archive category",
            [
                Text(Format("Archive {0}?", category.Path)),
                Text("Subcategories will also be archived.")
            ],
            () => () => store.SaveCategory(category.Id, category.Name, category.ParentId, true),
            DialogOptions.Confirmation("Archive"));
    }

    private async Task DeleteSelectedCategory()
    {
        if (OverviewPane.CategoriesCard.Selected is not { } category)
        {
            return;
        }

        await dialogs.ShowAsync("Delete category",
            [
                Text(Format("Delete {0}?", category.Path)),
                Text("Categories used by transactions or with subcategories must be archived instead.")
            ],
            () => () => store.DeleteCategory(category.Id),
            DialogOptions.Confirmation("Delete", () => CategoryRules.CheckDelete(OverviewPane.Data!.Snapshot.Categories, category.Id)));
    }

    private async Task EditCategory(Category? category)
    {
        var categoryArchived = category?.Archived ?? false;
        var name = Input(category?.Name ?? string.Empty);
        var options = new List<Choice<string?>> { new(null, Get("No parent (top-level)")) };
        options.AddRange(OverviewPane.Data!.Snapshot.Categories
            .Where(c => c.ParentId is null && !c.Archived && c.Id != category?.Id)
            .Select(c => new Choice<string?>(c.Id, c.Path)));

        if (category?.ParentId is { } current && options.All(c => c.Value != current))
        {
            var archivedParent = OverviewPane.Data!.Snapshot.Categories.Single(c => c.Id == current);
            options.Add(new Choice<string?>(current, ArchivedLabel.Of(archivedParent.Path, archivedParent.Archived)));
        }

        var parent = new ComboBox
        {
            ItemsSource = options,
            SelectedItem = options.FirstOrDefault(c => c.Value == category?.ParentId) ?? options[0],
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        await dialogs.ShowAsync(category is null ? "Add category" : "Edit category",
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
            new DialogOptions(Validate: () => CategoryRules.CheckSave(
                OverviewPane.Data!.Snapshot.Categories, category?.Id, name.Text ?? string.Empty, ((Choice<string?>)parent.SelectedItem!).Value, categoryArchived)));
    }
}
