using Avalonia.Controls;
using Avalonia.Media;
using Balancia.Core;

using static Balancia.Desktop.InputParsing;
using static Balancia.Desktop.Localization.UiText;

namespace Balancia.Desktop;

/// <summary>The top expenditures card: the largest expense categories as proportional bars. It draws what it is given and holds no store or window reference.</summary>
public partial class TopExpendituresCard : UserControl
{
    private static readonly IBrush MutedBrush = Brush.Parse("#71838D");

    private IReadOnlyList<CategoryTotal>? rendered;
    private string? renderedScope;

    public TopExpendituresCard() => InitializeComponent();

    /// <summary>
    /// Draws the bars, scaled to the largest total, with the current language. <paramref name="scopeName"/> is the category the
    /// overview is narrowed to when it has subcategories, and is added to the heading; null shows the plain heading.
    /// </summary>
    public void Render(IReadOnlyList<CategoryTotal> largest, string? scopeName)
    {
        rendered = [.. largest];
        renderedScope = scopeName;
        TopExpendituresBody.Children.Clear();
        TopExpendituresHeading.Text = scopeName is null
            ? Get("Top expenditures")
            : Format("Top expenditures · {0}", scopeName);

        if (largest.Count == 0)
        {
            TopExpendituresBody.Children.Add(new TextBlock
            {
                Text = Get("No matching expenses."),
                FontSize = 12,
                Foreground = MutedBrush,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            });
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
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            });
            GridLayout.Place(row, new ProgressBar
            {
                Minimum = 0,
                Maximum = maximum,
                Value = category.Amount.Centimes,
                Height = 7,
                Foreground = Brush.Parse("#3989A7"),
                Background = Brush.Parse("#E7F2F6"),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            }, 1);
            GridLayout.Place(row, new TextBlock
            {
                Text = AmountText(category.Amount),
                FontSize = 11,
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                FontWeight = FontWeight.SemiBold,
            }, 2);
            TopExpendituresBody.Children.Add(row);
        }
    }

    /// <summary>Like <see cref="Render"/>, but leaves the card alone when it already shows exactly these totals under this heading.</summary>
    public void RenderIfChanged(IReadOnlyList<CategoryTotal> largest, string? scopeName)
    {
        if (rendered is not null && scopeName == renderedScope && rendered.SequenceEqual(largest))
        {
            return;
        }

        Render(largest, scopeName);
    }
}
