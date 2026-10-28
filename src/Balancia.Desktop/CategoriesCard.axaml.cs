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

using static Balancia.Desktop.Localization.UiText;

namespace Balancia.Desktop;

/// <summary>
/// The categories card: the category tree with a checkbox per category that filters the overview, tinted by top-level group.
/// It draws what it is given and raises requests; dialogs, saving and the filter itself stay with the window.
/// </summary>
public partial class CategoriesCard : UserControl
{
    private readonly Dictionary<string, CheckBox> filterChecks = [];
    private readonly Dictionary<string, IBrush> groupBrushes = [];
    private Func<string, bool> isSelected = _ => false;
    private bool syncing;

    public CategoriesCard()
    {
        InitializeComponent();
        CategoriesList.Styles.Add(new Style(selector => selector.OfType<ListBoxItem>())
        {
            Setters =
            {
                new Setter(BackgroundProperty, new Binding(".")
                {
                    Converter = new FuncValueConverter<Choice<Category>, IBrush>(choice =>
                        choice is not null && groupBrushes.TryGetValue(choice.Value.ParentId ?? choice.Value.Id, out var brush)
                            ? brush : Palette.RowIvory),
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
                IsChecked = isSelected(category.Id),
            };
            AutomationProperties.SetName(check, category.Path);
            ToolTip.SetTip(check, category.Path);
            filterChecks[category.Id] = check;
            check.IsCheckedChanged += (_, _) =>
            {
                if (syncing)
                {
                    return;
                }

                if (check.FindAncestorOfType<ListBox>() is { } list)
                {
                    list.SelectedItem = choice;
                }

                FilterToggled?.Invoke(category.Id, check.IsChecked == true);
            };
            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                ColumnSpacing = 7,
                Margin = new Thickness(category.ParentId is null ? 0 : 15, 0, 0, 0),
            };
            row.Children.Add(check);
            GridLayout.Place(row, new TextBlock
            {
                Text = ArchivedLabel.Of(category.Name, category.Archived),
                VerticalAlignment = VerticalAlignment.Center,
            }, 1);
            return row;
        });
        CategoriesList.DoubleTapped += (_, _) =>
        {
            if (Selected is { } category)
            {
                EditRequested?.Invoke(category);
            }
        };
        CategorySelectAllButton.Click += (_, _) => SelectAllRequested?.Invoke();
        CategoryAddButton.Click += (_, _) => AddRequested?.Invoke();
        CategoryArchiveButton.Click += (_, _) => ArchiveRequested?.Invoke();
        CategoryRemoveButton.Click += (_, _) => RemoveRequested?.Invoke();
    }

    /// <summary>The add button was pressed.</summary>
    public event Action? AddRequested;

    /// <summary>The toggle-all button was pressed.</summary>
    public event Action? SelectAllRequested;

    /// <summary>The archive button was pressed; <see cref="Selected"/> says what, if anything, it refers to.</summary>
    public event Action? ArchiveRequested;

    /// <summary>The remove button was pressed; <see cref="Selected"/> says what, if anything, it refers to.</summary>
    public event Action? RemoveRequested;

    /// <summary>A row was double-tapped.</summary>
    public event Action<Category>? EditRequested;

    /// <summary>A row's checkbox was changed by the user: the category id and whether it is now selected.</summary>
    public event Action<string, bool>? FilterToggled;

    public Category? Selected => (CategoriesList.SelectedItem as Choice<Category>)?.Value;

    /// <summary>Shows the categories with the current language. <paramref name="isSelected"/> says which categories the overview is filtered to.</summary>
    public void Render(IReadOnlyList<Category> categories, Func<string, bool> isSelected)
    {
        this.isSelected = isSelected;
        CategoriesHeading.Text = Get("Categories");
        var selectionLabel = Get("Toggle all categories");
        ToolTip.SetTip(CategorySelectAllButton, selectionLabel);
        AutomationProperties.SetName(CategorySelectAllButton, selectionLabel);
        ToolTip.SetTip(CategoryAddButton, Get("Add category"));
        ToolTip.SetTip(CategoryArchiveButton, Get("Archive selected category"));
        ToolTip.SetTip(CategoryRemoveButton, Get("Delete selected category"));

        filterChecks.Clear();
        groupBrushes.Clear();
        foreach (var category in categories.Where(category => category.ParentId is null))
        {
            groupBrushes[category.Id] = groupBrushes.Count % 2 == 0 ? Palette.RowIvory : Palette.RowBlue;
        }

        CategoriesList.ItemsSource = categories
            .Select(category => new Choice<Category>(category, ArchivedLabel.Of(category.Path, category.Archived)))
            .ToArray();
    }

    /// <summary>Brings the checkboxes in line with the filter, without raising <see cref="FilterToggled"/>.</summary>
    public void SyncFilterChecks()
    {
        var wasSyncing = syncing;
        syncing = true;
        try
        {
            foreach (var (id, check) in filterChecks)
            {
                check.IsChecked = isSelected(id);
            }
        }
        finally
        {
            syncing = wasSyncing;
        }
    }
}
