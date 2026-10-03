using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Balancia.Core;
using Balancia.Storage;

using static Balancia.Desktop.Localization.UiText;

namespace Balancia.Desktop;

public partial class MainWindow
{
    private static Grid TransactionRow(TransactionsHit hit, string emptyDescriptionText = "")
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

        return TransactionRow(entry.Draft.Date.ToString("dd MMM yyyy", Culture),
            string.IsNullOrWhiteSpace(entry.Draft.Description) ? emptyDescriptionText : entry.Draft.Description,
            entry.CategoryPath ?? "—",
            entry.DestinationName is null ? entry.AccountName : $"{entry.AccountName} → {entry.DestinationName}",
            sign + AmountText(amount.Abs()), false, entry.Draft.Kind);
    }

    private static Grid TransactionRow(string date, string description, string category, string account, string amount,
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
                Text = header ? Get(values[i]) : values[i],
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
            .Select(a => new Choice<Account>(a, a.Name + (a.Archived ? Get(" (archived)") : "")))
            .ToArray();
        if (accounts.Length == 0)
        {
            SetStatus("Add an active account before entering transactions.");
            return;
        }
        var kinds = Enum.GetValues<TransactionKind>()
            .Select(value => new Choice<TransactionKind>(value, Get(value.ToString())))
            .ToArray();
        var kind = new ComboBox
        {
            ItemsSource = kinds,
            SelectedItem = kinds.Single(choice => choice.Value == (existing?.Kind ?? TransactionKind.Expense)),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var date = DateInput(existing?.Date ?? DateOnly.FromDateTime(DateTime.Today));
        var description = Input(existing?.Description ?? "");
        var descriptionHistory = (await Task.Run(() => store.ReadRecentDescriptions())).ToList();
        var descriptionInput = DescriptionInput(description, descriptionHistory);
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
            PlaceholderText = Get("Type or select a category"),
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
                var matches = string.IsNullOrWhiteSpace(query)
                    ? []
                    : CategorySuggestionRanking.Build(categoryPaths, recentCategoryPaths, query);
                selectedCategorySuggestionIndex = matches.Count > 0 ? 0 : -1;
                categorySuggestions = CategorySuggestionRanking.Highlight(matches, selectedCategorySuggestionIndex);
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

            switch (eventArgs.Key)
            {
                case Key.Down or Key.Up:
                {
                    var direction = eventArgs.Key == Key.Down ? 1 : -1;
                    selectedCategorySuggestionIndex = Math.Clamp(
                        selectedCategorySuggestionIndex + direction,
                        0,
                        categorySuggestions.Count - 1);
                    categorySuggestions = CategorySuggestionRanking.Highlight(categorySuggestions, selectedCategorySuggestionIndex);
                    RenderCategorySuggestions();
                    suggestionPopup.IsOpen = true;
                    eventArgs.Handled = true;
                    break;
                }
                case Key.Tab:
                    AcceptCategorySuggestion(categorySuggestions[
                        Math.Clamp(selectedCategorySuggestionIndex, 0, categorySuggestions.Count - 1)]);
                    break;
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
                if (!string.IsNullOrWhiteSpace(description.Text))
                {
                    descriptionHistory.RemoveAll(value => string.Equals(value, description.Text, StringComparison.OrdinalIgnoreCase));
                    descriptionHistory.Insert(0, description.Text);
                }
                description.Text = "";
                amount.Text = "";
                memo.Text = "";
                acceptedCategoryPath = null;
                categorySuggestions = [];
                selectedCategorySuggestionIndex = -1;
                suggestionRows.Children.Clear();
                suggestionPopup.IsOpen = false;
                category.Text = string.Empty;
                category.Focus();
            };
        }

        await EditDialog(entry is null ? "Add transaction" : "Edit transaction",
            [
                Field("Type", kind),
                Field("Date", date), categoryField,
                Field("Amount (positive)", amount),
                Field("Account", account), toField,
                Field("Description", descriptionInput),
                Field("Notes", memo)
            ],
            () =>
            {
                NormalizeAmount(amount);
                var type = ((Choice<TransactionKind>)kind.SelectedItem!).Value;
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
            }, initialFocus: category, onSaveAndContinue: continueAfterSave, validate: ValidateFields);
        return;

        string? ValidateFields()
        {
            var type = ((Choice<TransactionKind>)kind.SelectedItem!).Value;
            var transactionDate = ParseDate(date);
            var selectedAccount = ((Choice<Account>)account.SelectedItem!).Value;
            if (transactionDate < selectedAccount.OpeningDate)
            {
                return "Transaction date precedes the account's opening date.";
            }

            if (type == TransactionKind.Transfer)
            {
                if (destination.SelectedItem is Choice<Account> { Value.OpeningDate: var destinationOpeningDate } &&
                    transactionDate < destinationOpeningDate)
                {
                    return "Transaction date precedes the account's opening date.";
                }

                return null;
            }

            var path = ReadSelectedCategoryPath();
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            var parts = path.Split('/').Select(part => part.Trim()).ToArray();
            if (parts.Length > 2 || parts.Any(string.IsNullOrWhiteSpace))
            {
                return "Enter a category or Category / Subcategory with one name on each side of '/'.";
            }

            if (availableCategories.Any(c => string.Equals(c.Path, path, StringComparison.OrdinalIgnoreCase)))
            {
                return null;
            }

            return snapshot!.Categories.Any(c => string.Equals(c.Path, path, StringComparison.OrdinalIgnoreCase))
                ? "Restore the archived category before using it for another transaction."
                : "Choose an existing category path.";
        }

        void UpdateFields()
        {
            var transfer = (kind.SelectedItem as Choice<TransactionKind>)?.Value == TransactionKind.Transfer;
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
            TransactionRow(new TransactionsHit(entry, null), "(No description)"),
            Text("Remove this transaction? For a transfer, both account movements will be removed together.")
        ],
        () => () => store.DeleteTransaction(entry.Id), "Remove");

    private sealed record TransactionItem(TransactionsHit Hit)
    {
        public override string ToString()
        {
            var entry = Hit.Entry;
            return $"{entry.Draft.Date:yyyy-MM-dd} · {entry.Draft.Description} · {entry.Draft.Kind} · {AmountText(entry.Draft.Amount)} · {entry.AccountName}";
        }
    }

    private static Control CategorySuggestionRow(CategorySuggestion suggestion)
    {
        var body = new StackPanel();
        if (suggestion.Section is { } section)
        {
            var label = section switch
            {
                CategorySuggestionSection.TopMatch => "TOP MATCH",
                CategorySuggestionSection.Recent => "RECENT",
                _ => "Category suggestion header",
            };
            body.Children.Add(new TextBlock
            {
                Text = Get(label),
                FontSize = 10,
                FontWeight = FontWeight.SemiBold,
                Foreground = Brush.Parse(section == CategorySuggestionSection.TopMatch ? "#52636C" : "#71838D"),
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
