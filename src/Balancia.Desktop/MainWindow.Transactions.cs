using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Balancia.Core;
using Balancia.Storage;

namespace Balancia.Desktop;

public partial class MainWindow
{
    private static ListBox HistoryList(IReadOnlyList<HistoryHit> hits) => new()
    {
        ItemsSource = hits.Select(hit => new HistoryItem(hit)).ToArray(),
        ItemTemplate = new FuncDataTemplate<HistoryItem>((item, _) => HistoryRow(item.Hit), true),
        HorizontalAlignment = HorizontalAlignment.Stretch,
        Background = Brushes.Transparent,
        BorderThickness = new Thickness(0)
    };

    private static Grid HistoryRow(HistoryHit hit, string emptyDescriptionText = "")
    {
        var entry = hit.Entry;
        var effect = hit.AccountEffect;
        var amount = effect ?? entry.Draft.Amount;
        var sign = effect is { } accountEffect ? accountEffect < Money.Zero ? "− " : "+ " :
            entry.Draft.Kind switch
            {
                TransactionKind.Expense => "− ",
                TransactionKind.Income => "+ ",
                _ => "↔ "
            };

        return HistoryRow(entry.Draft.Date.ToString("dd MMM yyyy", CultureInfo.CurrentCulture),
            string.IsNullOrWhiteSpace(entry.Draft.Description) ? emptyDescriptionText : entry.Draft.Description,
            entry.CategoryPath ?? "—",
            entry.DestinationName is null ? entry.AccountName : $"{entry.AccountName} → {entry.DestinationName}",
            sign + AmountText(amount.Abs()), false, entry.Draft.Kind);
    }

    private static Grid HistoryRow(string date, string description, string category, string account, string amount,
        bool header, TransactionKind? kind = null)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("120,120,200,130,*"),
            MinHeight = header ? 31 : 20
        };
        var values = new[] { date, amount, category, account, description };
        var amountColor = kind switch
        {
            TransactionKind.Income => "#2C8B6D",
            TransactionKind.Expense => "#B95D4D",
            TransactionKind.Transfer => "#1B4F72",
            _ => "#263C48"
        };

        for (var i = 0; i < values.Length; i++)
        {
            var cell = new TextBlock
            {
                Text = values[i],
                FontSize = header ? 12 : 13,
                FontWeight = !header && i == 1 ? FontWeight.Bold : FontWeight.Normal,
                Foreground = Brush.Parse(header ? "#71838D" : i == 1 ? amountColor : "#263C48"),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextAlignment = i == 1 ? TextAlignment.Right : TextAlignment.Left,
                Margin = new Thickness(0, 0, 30, 0)
            };
            AddColumn(row, cell, i);
        }
        return row;
    }

    private async Task EditTransaction(LedgerEntry? entry)
    {
        var existing = entry?.Draft;
        var accounts = snapshot!.Accounts.Where(a => !a.Archived || a.Id == existing?.AccountId || a.Id == existing?.DestinationId)
            .Select(a => new Choice<Account>(a, a.Name + (a.Archived ? " (archived)" : "")))
            .ToArray();
        if (accounts.Length == 0)
        {
            SetStatus("Add an active account before entering transactions.");
            return;
        }
        var kind = new ComboBox
        {
            ItemsSource = Enum.GetValues<TransactionKind>(),
            SelectedItem = existing?.Kind ?? TransactionKind.Expense,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var date = DateInput(existing?.Date ?? DateOnly.FromDateTime(DateTime.Today));
        var description = Input(existing?.Description ?? "");
        var amount = Input(existing?.Amount.Francs.ToString("0.00", CultureInfo.InvariantCulture) ?? "");
        amount.LostFocus += (_, _) => NormalizeAmount(amount);
        var account = new ComboBox
        {
            ItemsSource = accounts,
            SelectedItem = accounts.FirstOrDefault(c => c.Value.Id == (existing?.AccountId ?? defaultAccountId ?? lastAccountId)) ?? accounts[0],
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var destination = new ComboBox
        {
            ItemsSource = accounts,
            SelectedItem = accounts.FirstOrDefault(c => c.Value.Id == existing?.DestinationId),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var availableCategories = snapshot.Categories
            .Where(c => !c.Archived || c.Id == existing?.CategoryId)
            .ToArray();
        var categoryPaths = availableCategories.Select(c => c.Path).ToList();
        var recentCategoryPaths = (await Task.Run(() => store.ReadRecentCategoryPaths())).ToList();
        IReadOnlyList<CategorySuggestion> categorySuggestions = [];
        var selectedCategorySuggestionIndex = -1;
        var acceptingCategorySuggestion = false;
        string? acceptedCategoryPath = null;
        var category = new TextBox
        {
            Text = snapshot.Categories.FirstOrDefault(c => c.Id == existing?.CategoryId)?.Path ?? "",
            PlaceholderText = "Type or select a category",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var suggestionRows = new StackPanel();
        var suggestionPopupContent = new Border
        {
            MinWidth = 300,
            Background = Brush.Parse("#E5E5E5"),
            BorderBrush = Brush.Parse("#BFC7CB"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8),
            Child = new ScrollViewer
            {
                MaxHeight = 240,
                Content = suggestionRows
            }
        };
        var suggestionPopup = new Popup
        {
            PlacementTarget = category,
            Placement = PlacementMode.Bottom,
            IsLightDismissEnabled = true,
            Child = suggestionPopupContent
        };
        var categoryInput = new Grid();
        categoryInput.Children.Add(category);
        categoryInput.Children.Add(suggestionPopup);
        var updatingCategorySuggestions = false;
        category.TextChanged += (_, _) =>
        {
            if (updatingCategorySuggestions || acceptingCategorySuggestion)
            {
                return;
            }

            var query = category.Text ?? string.Empty;
            if (acceptedCategoryPath is not null &&
                string.Equals(query, acceptedCategoryPath, StringComparison.Ordinal))
            {
                acceptedCategoryPath = null;
                suggestionPopup.IsOpen = false;
                return;
            }

            acceptedCategoryPath = null;
            updatingCategorySuggestions = true;
            try
            {
                IReadOnlyList<CategorySuggestion> matches = string.IsNullOrWhiteSpace(query)
                    ? []
                    : BuildCategorySuggestions(categoryPaths, recentCategoryPaths, query);
                selectedCategorySuggestionIndex = matches.Count > 0 ? 0 : -1;
                categorySuggestions = HighlightCategorySuggestion(matches, selectedCategorySuggestionIndex);
                RenderCategorySuggestions();
                suggestionPopup.IsOpen = matches.Count > 0;
            }
            finally
            {
                updatingCategorySuggestions = false;
            }
        };
        category.AddHandler(InputElement.KeyDownEvent, (_, eventArgs) =>
        {
            if (categorySuggestions.Count == 0)
            {
                return;
            }

            if (eventArgs.Key is Key.Down or Key.Up)
            {
                var direction = eventArgs.Key == Key.Down ? 1 : -1;
                selectedCategorySuggestionIndex = Math.Clamp(
                    selectedCategorySuggestionIndex + direction,
                    0,
                    categorySuggestions.Count - 1);
                categorySuggestions = HighlightCategorySuggestion(categorySuggestions, selectedCategorySuggestionIndex);
                RenderCategorySuggestions();
                suggestionPopup.IsOpen = true;
                eventArgs.Handled = true;
            }
            else if (eventArgs.Key == Key.Tab)
            {
                AcceptCategorySuggestion(categorySuggestions[
                    Math.Clamp(selectedCategorySuggestionIndex, 0, categorySuggestions.Count - 1)]);
            }
        }, RoutingStrategies.Tunnel);
        var memo = Input(existing?.Memo ?? "");
        var toField = Field("Destination account", destination);
        var categoryField = Field("Category / subcategory", categoryInput);
        kind.SelectionChanged += (_, _) => UpdateFields();
        UpdateFields();
        Action? continueAfterSave = null;
        if (entry is null)
        {
            continueAfterSave = () =>
            {
                var path = category.Text?.Trim();
                description.Text = "";
                amount.Text = "";
                memo.Text = "";
                category.Focus();
            };
        }

        await EditDialog(entry is null ? "Add transaction" : "Edit transaction",
            [
                Field("Type", kind),
                Field("Date", date), categoryField,
                Field("Amount (positive)", amount),
                Field("Account", account), toField,
                Field("Description", description),
                Field("Notes", memo)
            ],
            () =>
            {
                NormalizeAmount(amount);
                var type = (TransactionKind)kind.SelectedItem!;
                var draft = new TransactionDraft(
                    type,
                    ParseDate(date),
                    description.Text ?? "",
                    ParseMoney(amount),
                    ((Choice<Account>)account.SelectedItem!).Value.Id,
                    type == TransactionKind.Transfer ? (destination.SelectedItem as Choice<Account>)?.Value.Id : null,
                    Memo: memo.Text ?? "");
                var categoryPath = type == TransactionKind.Transfer ? null : ReadSelectedCategoryPath();
                lastAccountId = draft.AccountId;
                return () => store.SaveTransactionWithCategoryPath(entry?.Id, draft, categoryPath);
            }, initialFocus: category, onSaveAndContinue: continueAfterSave);
        return;

        void UpdateFields()
        {
            var transfer = kind.SelectedItem is TransactionKind.Transfer;
            toField.IsVisible = transfer;
            categoryField.IsVisible = !transfer;
        }

        string? ReadSelectedCategoryPath()
        {
            var path = category.Text?.Trim();
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            var matchingCategory = availableCategories.FirstOrDefault(candidate =>
                string.Equals(candidate.Path, path, StringComparison.OrdinalIgnoreCase));
            return matchingCategory?.Path ?? path;
        }

        void AcceptCategorySuggestion(CategorySuggestion suggestion)
        {
            acceptedCategoryPath = suggestion.Path;
            acceptingCategorySuggestion = true;
            try
            {
                category.Text = suggestion.Path;
                suggestionPopup.IsOpen = false;
            }
            finally
            {
                acceptingCategorySuggestion = false;
            }
        }

        void RenderCategorySuggestions()
        {
            suggestionRows.Children.Clear();
            if (category.Bounds.Width > 0)
            {
                suggestionPopupContent.Width = category.Bounds.Width;
            }

            foreach (var suggestion in categorySuggestions)
            {
                var row = CategorySuggestionRow(suggestion);
                var button = new Button
                {
                    Content = row,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Padding = new Thickness(0),
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0)
                };
                button.Click += (_, _) => AcceptCategorySuggestion(suggestion);
                suggestionRows.Children.Add(button);
            }
        }
    }

    private async Task RemoveTransaction(LedgerEntry entry) => await EditDialog("Remove transaction",
        [
            HistoryRow(new HistoryHit(entry, null), "(No description)"),
            Text("Remove this transaction? For a transfer, both account movements will be removed together.")
        ],
        () => () => store.DeleteTransaction(entry.Id), "Remove");

    private sealed record HistoryItem(HistoryHit Hit)
    {
        public override string ToString()
        {
            var entry = Hit.Entry;
            return $"{entry.Draft.Date:yyyy-MM-dd} · {entry.Draft.Description} · {entry.Draft.Kind} · {AmountText(entry.Draft.Amount)} · {entry.AccountName}";
        }
    }

    private sealed record CategorySuggestion(string Path, string? Section, bool IsTopMatch, bool IsKeyboardSelected = false)
    {
        public override string ToString() => Path;
    }

    private static IReadOnlyList<CategorySuggestion> BuildCategorySuggestions(
        IEnumerable<string> categoryPaths,
        IEnumerable<string> recentCategoryPaths,
        string query)
    {
        var normalizedQuery = query.Trim();
        var matches = categoryPaths
            .Where(path => normalizedQuery.Length == 0 || path.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var suggestions = new List<CategorySuggestion>();
        string? topMatch = null;

        if (normalizedQuery.Length > 0)
        {
            topMatch = matches
                .OrderBy(path => CategoryMatchRank(path, normalizedQuery))
                .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (topMatch is not null)
            {
                suggestions.Add(new CategorySuggestion(topMatch, "TOP MATCH", true));
            }
        }

        var recentMatches = recentCategoryPaths
            .Where(path => matches.Contains(path, StringComparer.OrdinalIgnoreCase))
            .Where(path => !string.Equals(path, topMatch, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        suggestions.AddRange(recentMatches.Select((path, index) =>
            new CategorySuggestion(path, index == 0 ? "RECENT" : null, false)));

        var recentSet = recentMatches.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var remaining = matches
            .Where(path => !string.Equals(path, topMatch, StringComparison.OrdinalIgnoreCase) && !recentSet.Contains(path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        suggestions.AddRange(remaining.Select((path, index) =>
            new CategorySuggestion(path, index == 0 ? "CATEGORIES" : null, false)));
        return suggestions;
    }

    private static IReadOnlyList<CategorySuggestion> HighlightCategorySuggestion(
        IReadOnlyList<CategorySuggestion> suggestions,
        int selectedIndex) => suggestions
        .Select((suggestion, index) => suggestion with { IsKeyboardSelected = index == selectedIndex })
        .ToArray();

    private static int CategoryMatchRank(string path, string query)
    {
        if (string.Equals(path, query, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (path.StartsWith(query, StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        return path.Split('/').Any(part => part.TrimStart().StartsWith(query, StringComparison.OrdinalIgnoreCase)) ? 2 : 3;
    }

    private static Control CategorySuggestionRow(CategorySuggestion suggestion)
    {
        var body = new StackPanel();
        if (suggestion.Section is not null)
        {
            body.Children.Add(new TextBlock
            {
                Text = suggestion.Section,
                FontSize = 10,
                FontWeight = FontWeight.SemiBold,
                Foreground = Brush.Parse(suggestion.IsTopMatch ? "#52636C" : "#71838D"),
                Margin = new Thickness(2, 4, 2, 2)
            });
        }

        body.Children.Add(new Border
        {
            Background = suggestion.IsKeyboardSelected ? Brush.Parse("#3989A7") : Brushes.Transparent,
            Padding = new Thickness(8, 6),
            Child = new TextBlock
            {
                Text = suggestion.Path,
                Foreground = suggestion.IsKeyboardSelected ? Brushes.White : Brush.Parse("#263C48")
            }
        });
        return body;
    }

}
