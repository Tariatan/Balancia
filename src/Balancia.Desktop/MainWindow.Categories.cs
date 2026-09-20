using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Balancia.Core;
using Balancia.Storage;
using Microsoft.Data.Sqlite;

namespace Balancia.Desktop;

public partial class MainWindow
{
    private Border CategoriesPanel(LedgerSnapshot snapshot)
    {
        var body = new StackPanel
        {
            Spacing = 1
        };

        body.Children.Add(SectionHeader("Largest expense categories", "View all →", () => Navigate("Categories")));

        if (snapshot.LargestCategories.Count == 0)
        {
            body.Children.Add(QuietText("No expenses in this period.", 12));
        }

        var maximum = snapshot.LargestCategories.FirstOrDefault()?.Amount.Centimes ?? 1;

        foreach (var category in snapshot.LargestCategories)
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
        return Panel(body);
    }

    private void RenderCategories(LedgerSnapshot snapshot)
    {
        var layout = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*"),
            RowSpacing = 11
        };
        AddRow(layout, Text("Choose an optional parent for a subcategory. Archiving a parent also archives its children."), 0);
        var categories = new ListBox
        {
            ItemsSource = snapshot.Categories,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0)
        };
        var actions = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3 };
        var add = ActionButton("+", () => EditCategory(null));
        add.Width = 30;
        add.Padding = new Thickness(0);
        add.FontSize = 18;
        var archive = ActionButton("▣", () => ArchiveSelectedCategory(categories));
        archive.Width = 30;
        archive.Padding = new Thickness(0);
        archive.FontSize = 14;
        var remove = ActionButton("🗑", () => DeleteSelectedCategory(categories));
        remove.Width = 30;
        remove.Padding = new Thickness(0);
        remove.FontSize = 14;
        ToolTip.SetTip(add, "Add category");
        ToolTip.SetTip(archive, "Archive selected category");
        ToolTip.SetTip(remove, "Delete selected category");
        buttons.Children.Add(add);
        buttons.Children.Add(archive);
        buttons.Children.Add(remove);
        AddColumn(actions, buttons, 1);
        AddRow(layout, actions, 1);
        categories.DoubleTapped += async (_, _) =>
        {
            if (categories.SelectedItem is Category category)
            {
                await EditCategory(category);
            }
        };
        AddRow(layout, Panel(categories), 2);
        ResponsiveBody.Content = layout;
    }

    private async Task ArchiveSelectedCategory(ListBox categories)
    {
        if (categories.SelectedItem is not Category category)
        {
            return;
        }

        await EditDialog("Archive category",
            [Text($"Archive {category.Path}?"), Text("Subcategories will also be archived.")],
            () => () => _store.SaveCategory(category.Id, category.Name, category.ParentId, true), "Archive");
    }

    private async Task DeleteSelectedCategory(ListBox categories)
    {
        if (categories.SelectedItem is not Category category)
        {
            return;
        }

        await EditDialog("Delete category",
            [Text($"Delete {category.Path}?"), Text("Categories used by transactions or with subcategories must be archived instead.")],
            () => () => _store.DeleteCategory(category.Id), "Delete");
    }

    private async Task EditCategory(Category? category)
    {
        var name = Input(category?.Name ?? "");
        var options = new List<Choice<string?>> { new(null, "No parent (top-level)") };
        options.AddRange(_snapshot!.Categories.Where(c => c.ParentId is null && !c.Archived && c.Id != category?.Id).Select(c => new Choice<string?>(c.Id, c.Path)));
        if (category?.ParentId is { } current && options.All(c => c.Value != current))
        {
            options.Add(new(current, _snapshot.Categories.Single(c => c.Id == current).ToString()));
        }

        var parent = new ComboBox
        {
            ItemsSource = options,
            SelectedItem = options.FirstOrDefault(c => c.Value == category?.ParentId) ?? options[0],
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        await EditDialog(category is null ? "Add category" : "Edit category",
            [Field("Name", name), Field("Parent category", parent)],
            () =>
            {
                var values = (name.Text ?? "", ((Choice<string?>)parent.SelectedItem!).Value, category?.Archived ?? false);
                return () => _store.SaveCategory(category?.Id, values.Item1, values.Item2, values.Item3);
            });
    }
}
