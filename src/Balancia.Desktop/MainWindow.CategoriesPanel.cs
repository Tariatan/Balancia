using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Balancia.Core;

using static Balancia.Desktop.Localization.UiText;

namespace Balancia.Desktop;

public partial class MainWindow
{
    private void FillCategoriesPanel(StackPanel body, LedgerSnapshot ledgerSnapshot)
    {
        body.Children.Clear();
        renderedCategories = [.. ledgerSnapshot.LargestCategories];
        renderedCategoryId = filter.CategoryId;
        var selectedCategory = ledgerSnapshot.Categories.FirstOrDefault(c => c.Id == filter.CategoryId);
        var hasSubcategories = selectedCategory is not null &&
            ledgerSnapshot.Categories.Any(c => c.ParentId == selectedCategory.Id);
        var title = hasSubcategories
            ? Format("Top expenditures · {0}", selectedCategory!.Name)
            : Get("Top expenditures");
        CategoryHeading.Text = title;

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
}
