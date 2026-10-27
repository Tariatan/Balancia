using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Balancia.Core;

using static Balancia.Desktop.InputParsing;
using static Balancia.Desktop.Localization.UiText;

namespace Balancia.Desktop;

public partial class MainWindow
{
    private readonly Dictionary<string, CheckBox> categoryFilterChecks = [];
    private readonly Dictionary<string, IBrush> categoryGroupBrushes = [];
    private IReadOnlyList<CategoryTotal> renderedCategories = [];
    private string? renderedCategoryId;

    private Category? SelectedCategory => (CategoriesList.SelectedItem as Choice<Category>)?.Value;

    private static string WithArchivedMarker(string text, bool archived) =>
        archived ? text + Get(" (archived)") : text;

    private void InitializeCategoryPanel()
    {
        CategoriesList.Styles.Add(new Style(selector => selector.OfType<ListBoxItem>())
        {
            Setters =
            {
                new Setter(BackgroundProperty, new Binding(".")
                {
                    Converter = new FuncValueConverter<Choice<Category>, IBrush>(choice =>
                        choice is not null && categoryGroupBrushes.TryGetValue(choice.Value.ParentId ?? choice.Value.Id, out var brush)
                            ? brush : DayIvoryBrush),
                }),
            },
        });
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
                IsChecked = overviewState.IsCategorySelected(category.Id),
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
                Text = WithArchivedMarker(category.Name, category.Archived),
                VerticalAlignment = VerticalAlignment.Center,
            };
            GridLayout.Place(row, name, 1);
            return row;
        });
        CategoriesList.DoubleTapped += async (_, _) =>
        {
            if (SelectedCategory is { } category)
            {
                await EditCategory(category);
            }
        };
        CategorySelectAllButton.Click += async (_, _) => await ToggleAllCategoryFilters();
        CategoryAddButton.Click += async (_, _) => await EditCategory(null);
        CategoryArchiveButton.Click += async (_, _) => await ArchiveSelectedCategory();
        CategoryRemoveButton.Click += async (_, _) => await DeleteSelectedCategory();
    }

    private void RenderCategoryPanel(LedgerSnapshot ledgerSnapshot)
    {
        CategoriesHeading.Text = Get("Categories");
        var selectionLabel = Get("Toggle all categories");
        ToolTip.SetTip(CategorySelectAllButton, selectionLabel);
        AutomationProperties.SetName(CategorySelectAllButton, selectionLabel);
        ToolTip.SetTip(CategoryAddButton, Get("Add category"));
        ToolTip.SetTip(CategoryArchiveButton, Get("Archive selected category"));
        ToolTip.SetTip(CategoryRemoveButton, Get("Delete selected category"));

        categoryFilterChecks.Clear();
        categoryGroupBrushes.Clear();
        foreach (var category in ledgerSnapshot.Categories.Where(category => category.ParentId is null))
        {
            categoryGroupBrushes[category.Id] = categoryGroupBrushes.Count % 2 == 0 ? DayIvoryBrush : DayBlueBrush;
        }
        CategoriesList.ItemsSource = ledgerSnapshot.Categories
            .Select(category => new Choice<Category>(category,
                WithArchivedMarker(category.Path, category.Archived)))
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
                check.IsChecked = overviewState.IsCategorySelected(id);
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

        overviewState.ToggleCategory(id, selected, overview!.Snapshot.Categories);
        SyncCategoryFilterChecks();
        await RequestOverviewFilterRefresh();
    }

    private async Task ToggleAllCategoryFilters()
    {
        if (updatingFilterControls || overview is null)
        {
            return;
        }

        overviewState.ToggleAllCategories(overview.Snapshot.Categories);
        SyncCategoryFilterChecks();
        await RequestOverviewFilterRefresh();
    }

    private async Task ArchiveSelectedCategory()
    {
        if (SelectedCategory is not { } category)
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
        if (SelectedCategory is not { } category)
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
            options.Add(new Choice<string?>(current, WithArchivedMarker(archivedParent.Path, archivedParent.Archived)));
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

    private void FillCategoriesPanel(StackPanel body, LedgerSnapshot ledgerSnapshot)
    {
        body.Children.Clear();
        renderedCategories = [.. ledgerSnapshot.LargestCategories];
        renderedCategoryId = overviewState.Filter.CategoryId;

        var filtered = ledgerSnapshot.Categories.FirstOrDefault(c => c.Id == overviewState.Filter.CategoryId);
        var hasSubcategories = filtered is not null && ledgerSnapshot.Categories.Any(c => c.ParentId == filtered.Id);
        CategoryHeading.Text = hasSubcategories
            ? Format("Top expenditures · {0}", filtered!.Name)
            : Get("Top expenditures");

        var largest = ledgerSnapshot.LargestCategories;
        if (largest.Count == 0)
        {
            body.Children.Add(QuietText("No matching expenses.", 12));
            return;
        }

        var maximum = largest[0].Amount.Centimes;
        foreach (var category in largest)
        {
            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("75,*,80"),
                MinHeight = 20,
            };
            row.Children.Add(new TextBlock
            {
                Text = category.Name,
                FontSize = 11,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            });

            GridLayout.Place(row, new ProgressBar
            {
                Minimum = 0,
                Maximum = maximum,
                Value = category.Amount.Centimes,
                Height = 7,
                Foreground = Brush.Parse("#3989A7"),
                Background = Brush.Parse("#E7F2F6"),
                VerticalAlignment = VerticalAlignment.Center,
            }, 1);
            GridLayout.Place(row, new TextBlock
            {
                Text = AmountText(category.Amount),
                FontSize = 11,
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeight.SemiBold,
            }, 2);
            body.Children.Add(row);
        }
    }
}
