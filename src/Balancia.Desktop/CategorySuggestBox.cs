using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Balancia.Core;

using static Balancia.Desktop.Localization.UiText;

namespace Balancia.Desktop;

/// <summary>
/// A text box with a popup of category paths ranked for what was typed: the top match, then the paths the user uses regularly, then the rest.
/// Up and Down move the highlight, Tab accepts it, and clicking a suggestion accepts it.
/// </summary>
internal sealed class CategorySuggestBox : Grid
{
    private static readonly IBrush SuggestionSelectedBrush = Brush.Parse("#3989A7");
    private static readonly IBrush SuggestionTopMatchBrush = Brush.Parse("#52636C");

    private readonly TextBox category;
    private readonly IReadOnlyList<string> categoryPaths;
    private readonly StackPanel rows = new();
    private readonly Border popupContent;
    private readonly Popup popup;
    private IReadOnlyList<string> regularCategoryPaths;
    private IReadOnlyList<CategorySuggestion> suggestions = [];
    private int selectedIndex = -1;
    private bool accepting;
    private string? acceptedPath;

    public CategorySuggestBox(TextBox category, IReadOnlyList<string> categoryPaths, IReadOnlyList<string> regularCategoryPaths)
    {
        this.category = category;
        this.categoryPaths = categoryPaths;
        this.regularCategoryPaths = regularCategoryPaths;
        popupContent = new Border
        {
            MinWidth = 300,
            Background = Brush.Parse("#E5E5E5"),
            BorderBrush = Brush.Parse("#BFC7CB"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8),
            Child = new ScrollViewer { MaxHeight = 240, Content = rows }
        };
        popup = new Popup
        {
            PlacementTarget = category,
            Placement = PlacementMode.Bottom,
            IsLightDismissEnabled = true,
            Child = popupContent
        };
        Children.Add(category);
        Children.Add(popup);

        category.TextChanged += (_, _) => HandleTextChanged();
        category.AddHandler(KeyDownEvent, (_, eventArgs) => HandleKeyDown(eventArgs), RoutingStrategies.Tunnel);
    }

    /// <summary>The suggestions currently offered, in order, with the highlighted one marked.</summary>
    internal IReadOnlyList<CategorySuggestion> Suggestions => suggestions;

    /// <summary>The offered rows, one button per suggestion.</summary>
    internal IReadOnlyList<Control> Rows => rows.Children;

    internal int SelectedIndex => selectedIndex;

    internal bool IsOpen => popup.IsOpen;

    /// <summary>Clears the box and its suggestions and replaces the regular-category list used for ranking.</summary>
    public void Reset(IReadOnlyList<string> newRegularCategoryPaths)
    {
        regularCategoryPaths = newRegularCategoryPaths;
        acceptedPath = null;
        suggestions = [];
        selectedIndex = -1;
        rows.Children.Clear();
        popup.IsOpen = false;
        category.Text = string.Empty;
    }

    private void HandleTextChanged()
    {
        if (accepting)
        {
            return;
        }

        var query = category.Text ?? string.Empty;
        var justAccepted = acceptedPath is not null && string.Equals(query, acceptedPath, StringComparison.Ordinal);
        acceptedPath = null;
        if (justAccepted)
        {
            popup.IsOpen = false;
            return;
        }

        var matches = string.IsNullOrWhiteSpace(query)
            ? []
            : CategorySuggestionRanking.Build(categoryPaths, regularCategoryPaths, query);
        selectedIndex = matches.Count > 0 ? 0 : -1;
        suggestions = CategorySuggestionRanking.Highlight(matches, selectedIndex);
        Render();
        popup.IsOpen = matches.Count > 0;
    }

    private void HandleKeyDown(KeyEventArgs eventArgs)
    {
        if (suggestions.Count == 0)
        {
            return;
        }

        switch (eventArgs.Key)
        {
            case Key.Down or Key.Up:
                selectedIndex = Math.Clamp(selectedIndex + (eventArgs.Key == Key.Down ? 1 : -1), 0, suggestions.Count - 1);
                suggestions = CategorySuggestionRanking.Highlight(suggestions, selectedIndex);
                Render();
                popup.IsOpen = true;
                eventArgs.Handled = true;
                break;
            case Key.Tab:
                Accept(suggestions[Math.Clamp(selectedIndex, 0, suggestions.Count - 1)]);
                break;
        }
    }

    private void Accept(CategorySuggestion suggestion)
    {
        acceptedPath = suggestion.Path;
        accepting = true;
        try
        {
            category.Text = suggestion.Path;
            popup.IsOpen = false;
        }
        finally
        {
            accepting = false;
        }
    }

    private void Render()
    {
        rows.Children.Clear();
        if (category.Bounds.Width > 0)
        {
            popupContent.Width = category.Bounds.Width;
        }

        foreach (var suggestion in suggestions)
        {
            var button = SuggestionButton(suggestion, () => Accept(suggestion));
            rows.Children.Add(button);
            if (suggestion.IsKeyboardSelected)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (popup.IsOpen && rows.Children.Contains(button))
                    {
                        button.BringIntoView();
                    }
                }, DispatcherPriority.Loaded);
            }
        }
    }

    private static Button SuggestionButton(CategorySuggestion suggestion, Action accept)
    {
        var button = new Button
        {
            Content = SuggestionRow(suggestion),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0)
        };
        button.Click += (_, _) => accept();
        return button;
    }

    private static StackPanel SuggestionRow(CategorySuggestion suggestion)
    {
        var body = new StackPanel();
        if (suggestion.Section is { } section)
        {
            var label = section switch
            {
                CategorySuggestionSection.TopMatch => "TOP MATCH",
                CategorySuggestionSection.Regular => "REGULAR",
                _ => "Category suggestion header",
            };
            body.Children.Add(new TextBlock
            {
                Text = Get(label),
                FontSize = 10,
                FontWeight = FontWeight.SemiBold,
                Foreground = section == CategorySuggestionSection.TopMatch ? SuggestionTopMatchBrush : TransactionRows.HeaderBrush,
                Margin = new Thickness(2, 4, 2, 2)
            });
        }

        body.Children.Add(new Border
        {
            Background = suggestion.IsKeyboardSelected ? SuggestionSelectedBrush : Brushes.Transparent,
            Padding = new Thickness(8, 6),
            Child = new TextBlock
            {
                Text = suggestion.Path,
                Foreground = suggestion.IsKeyboardSelected ? Brushes.White : TransactionRows.TextBrush
            }
        });
        return body;
    }
}
