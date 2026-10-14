using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Balancia.Core;
using Balancia.Storage;

using static Balancia.Desktop.InputParsing;
using static Balancia.Desktop.Localization.UiText;

namespace Balancia.Desktop;

public partial class MainWindow
{
    private const int TransactionsPageSize = 100;

    private static readonly IBrush RowTextBrush = Brush.Parse("#263C48");
    private static readonly IBrush RowHeaderBrush = Brush.Parse("#71838D");
    private static readonly IBrush IncomeBrush = Brush.Parse("#2C8B6D");
    private static readonly IBrush ExpenseBrush = Brush.Parse("#B95D4D");
    private static readonly IBrush TransferBrush = Brush.Parse("#1B4F72");
    private static readonly IBrush SuggestionSelectedBrush = Brush.Parse("#3989A7");
    private static readonly IBrush SuggestionTopMatchBrush = Brush.Parse("#52636C");

    private TransactionsPage? transactions;
    private int offset;

    private LedgerEntry? SelectedTransaction => (TransactionsList.SelectedItem as TransactionItem)?.Hit.Entry;

    private void InitializeTransactionsPanel()
    {
        TransactionsList.ItemTemplate = new FuncDataTemplate<TransactionItem>((item, _) => TransactionRow(item.Hit), true);
        TransactionsList.DoubleTapped += async (_, _) =>
        {
            if (SelectedTransaction is { } entry)
            {
                await EditTransaction(entry);
            }
        };
        TransactionsAddButton.Click += async (_, _) => await EditTransaction(null);
        TransactionsRemoveButton.Click += async (_, _) =>
        {
            if (SelectedTransaction is { } entry)
            {
                await RemoveTransaction(entry);
            }
        };
        TransactionsPreviousPage.Click += async (_, _) => await ChangeTransactionsPage(-TransactionsPageSize);
        TransactionsNextPage.Click += async (_, _) => await ChangeTransactionsPage(TransactionsPageSize);
    }

    private void LocalizeTransactionsPanel()
    {
        TransactionsColumnDate.Text = Get("Date");
        TransactionsColumnAmount.Text = Get("Amount");
        TransactionsColumnCategory.Text = Get("Category");
        TransactionsColumnAccount.Text = Get("Account");
        TransactionsColumnDescription.Text = Get("Description");
        TransactionsEmpty.Text = Get("No matching transactions.");
        ToolTip.SetTip(TransactionsAddButton, Get("Add transaction"));
        ToolTip.SetTip(TransactionsRemoveButton, Get("Delete selected transaction"));
    }

    private async Task ChangeTransactionsPage(int delta)
    {
        offset = Math.Max(0, offset + delta);
        await RequestOverviewFilterRefresh();
        if (TransactionsList.Scroll is { } scroll)
        {
            scroll.Offset = new Vector(scroll.Offset.X, 0);
        }
    }

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
        var amountBrush = kind switch
        {
            TransactionKind.Income => IncomeBrush,
            TransactionKind.Expense => ExpenseBrush,
            TransactionKind.Transfer => TransferBrush,
            _ => RowTextBrush
        };

        for (var i = 0; i < values.Length; i++)
        {
            var isAmount = i == 1;
            var cell = new TextBlock
            {
                Text = header ? Get(values[i]) : values[i],
                FontSize = header ? 12 : 13,
                FontWeight = !header && isAmount ? FontWeight.Bold : FontWeight.Normal,
                Foreground = header ? RowHeaderBrush : isAmount ? amountBrush : RowTextBrush,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextAlignment = isAmount ? TextAlignment.Right : TextAlignment.Left,
                Margin = new Thickness(0, 0, 30, 0)
            };
            AddColumn(row, cell, i);
        }
        return row;
    }

    private async Task EditTransaction(LedgerEntry? entry)
    {
        var existing = entry?.Draft;
        var accounts = AccountChoices(existing);

        if (accounts.Length == 0)
        {
            SetStatus("Add an active account before entering transactions.");
            return;
        }

        var date = DateInput(existing?.Date ?? Today);
        var description = Input(existing?.Description ?? "");
        var descriptionHistory = (await Task.Run(() => store.ReadRecentDescriptions())).ToList();
        var descriptionInput = DescriptionInput(description, descriptionHistory);

        var amount = Input(existing?.Amount.Francs.ToString("0.00", CultureInfo.InvariantCulture) ?? "");
        amount.LostFocus += (_, _) => NormalizeAmount(amount);

        var account = ChoiceBox(accounts,
            accounts.FirstOrDefault(c => c.Value.Id == (existing?.AccountId ?? defaultAccountId ?? lastAccountId)) ?? accounts[0]);

        var destination = ChoiceBox(accounts, accounts.FirstOrDefault(c => c.Value.Id == existing?.DestinationId));

        var availableCategories = snapshot!.Categories
            .Where(c => !c.Archived || c.Id == existing?.CategoryId)
            .ToArray();

        var category = new TextBox
        {
            Text = snapshot.Categories.FirstOrDefault(c => c.Id == existing?.CategoryId)?.Path ?? "",
            PlaceholderText = Get("Type or select a category"),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var (categoryInput, resetCategoryInput) = CategoryInput(category,
            [.. availableCategories.Select(c => c.Path)],
            await Task.Run(() => store.ReadRegularCategoryPaths()));
        var memo = Input(existing?.Memo ?? "");
        var toField = Field("Destination account", destination);
        var categoryField = Field("Category / subcategory", categoryInput);

        var kinds = Enum.GetValues<TransactionKind>()
            .Select(value => new Choice<TransactionKind>(value, Get(value.ToString())))
            .ToArray();
        var kind = ChoiceBox(kinds, kinds.Single(choice => choice.Value == (existing?.Kind ?? TransactionKind.Expense)));
        kind.SelectionChanged += (_, _) => UpdateFields();

        UpdateFields();

        Func<Task>? continueAfterSave = null;
        if (entry is null)
        {
            continueAfterSave = async () =>
            {
                if (!string.IsNullOrWhiteSpace(description.Text))
                {
                    descriptionHistory.RemoveAll(value => string.Equals(value, description.Text, StringComparison.OrdinalIgnoreCase));
                    descriptionHistory.Insert(0, description.Text);
                }
                var regularCategoryPaths = await Task.Run(() => store.ReadRegularCategoryPaths());
                description.Text = "";
                amount.Text = "";
                memo.Text = "";
                resetCategoryInput(regularCategoryPaths);
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
                var type = SelectedValue<TransactionKind>(kind);
                var draft = new TransactionDraft(
                    type,
                    ParseDate(date),
                    description.Text ?? "",
                    ParseMoney(amount),
                    SelectedValue<Account>(account).Id,
                    type == TransactionKind.Transfer ? (destination.SelectedItem as Choice<Account>)?.Value.Id : null,
                    Memo: memo.Text ?? "");
                var categoryPath = type == TransactionKind.Transfer ? null : ReadCategoryPath(category, availableCategories);
                lastAccountId = draft.AccountId;
                return () => store.SaveTransactionWithCategoryPath(entry?.Id, draft, categoryPath);
            },
            new DialogOptions(DialogSize.Tall, InitialFocus: category, OnSaveAndContinue: continueAfterSave, Validate: ValidateFields));
        return;

        string? ValidateFields()
        {
            var type = SelectedValue<TransactionKind>(kind);
            var transactionDate = ParseDate(date);
            var selectedAccount = SelectedValue<Account>(account);
            var selectedDestination = type == TransactionKind.Transfer ? (destination.SelectedItem as Choice<Account>)?.Value : null;
            if (transactionDate < selectedAccount.OpeningDate || transactionDate < selectedDestination?.OpeningDate)
            {
                return "Transaction date precedes the account's opening date.";
            }

            return type == TransactionKind.Transfer ? null : CategoryPathError(ReadCategoryPath(category, availableCategories), availableCategories);
        }

        void UpdateFields()
        {
            var transfer = (kind.SelectedItem as Choice<TransactionKind>)?.Value == TransactionKind.Transfer;
            toField.IsVisible = transfer;
            categoryField.IsVisible = !transfer;
        }
    }

    private Choice<Account>[] AccountChoices(TransactionDraft? existing) =>
    [
        .. snapshot!.Accounts
            .Where(a => !a.Archived || a.Id == existing?.AccountId || a.Id == existing?.DestinationId)
            .Select(a => new Choice<Account>(a, a.Name + (a.Archived ? Get(" (archived)") : "")))
    ];

    private static ComboBox ChoiceBox<T>(Choice<T>[] choices, Choice<T>? selected) => new()
    {
        ItemsSource = choices,
        SelectedItem = selected,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };

    private static T SelectedValue<T>(ComboBox box) => ((Choice<T>)box.SelectedItem!).Value;

    private static string? ReadCategoryPath(TextBox category, IReadOnlyList<Category> availableCategories)
    {
        var path = category.Text?.Trim();
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        return availableCategories.FirstOrDefault(candidate =>
            string.Equals(candidate.Path, path, StringComparison.OrdinalIgnoreCase))?.Path ?? path;
    }

    private string? CategoryPathError(string? path, IReadOnlyList<Category> availableCategories)
    {
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

    /// <summary>
    /// Wraps the category box with a suggestion popup. The returned action clears the box and its suggestions and
    /// replaces the regular-category list used for ranking.
    /// </summary>
    private static (Grid Input, Action<IReadOnlyList<string>> Reset) CategoryInput(
        TextBox category, IReadOnlyList<string> categoryPaths, IReadOnlyList<string> regularCategoryPaths)
    {
        IReadOnlyList<CategorySuggestion> suggestions = [];
        var selectedIndex = -1;
        var accepting = false;
        string? acceptedPath = null;
        var rows = new StackPanel();
        var popupContent = new Border
        {
            MinWidth = 300,
            Background = Brush.Parse("#E5E5E5"),
            BorderBrush = Brush.Parse("#BFC7CB"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8),
            Child = new ScrollViewer { MaxHeight = 240, Content = rows }
        };
        var popup = new Popup
        {
            PlacementTarget = category,
            Placement = PlacementMode.Bottom,
            IsLightDismissEnabled = true,
            Child = popupContent
        };
        var input = new Grid();
        input.Children.Add(category);
        input.Children.Add(popup);

        category.TextChanged += (_, _) =>
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
        };
        category.AddHandler(KeyDownEvent, (_, eventArgs) =>
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
        }, RoutingStrategies.Tunnel);

        return (input, Reset);

        void Reset(IReadOnlyList<string> newRegularCategoryPaths)
        {
            regularCategoryPaths = newRegularCategoryPaths;
            acceptedPath = null;
            suggestions = [];
            selectedIndex = -1;
            rows.Children.Clear();
            popup.IsOpen = false;
            category.Text = string.Empty;
        }

        void Accept(CategorySuggestion suggestion)
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

        void Render()
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
    }

    private static Button SuggestionButton(CategorySuggestion suggestion, Action accept)
    {
        var button = new Button
        {
            Content = CategorySuggestionRow(suggestion),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0)
        };
        button.Click += (_, _) => accept();
        return button;
    }

    private Task RemoveTransaction(LedgerEntry entry) => EditDialog("Remove transaction",
        [
            TransactionRow(new TransactionsHit(entry, null), "(No description)"),
            Text("Remove this transaction? For a transfer, both account movements will be removed together.")
        ],
        () => () => store.DeleteTransaction(entry.Id), DialogOptions.Confirmation("Remove", size: DialogSize.Wide));

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
                CategorySuggestionSection.Regular => "REGULAR",
                _ => "Category suggestion header",
            };
            body.Children.Add(new TextBlock
            {
                Text = Get(label),
                FontSize = 10,
                FontWeight = FontWeight.SemiBold,
                Foreground = section == CategorySuggestionSection.TopMatch ? SuggestionTopMatchBrush : RowHeaderBrush,
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
                Foreground = suggestion.IsKeyboardSelected ? Brushes.White : RowTextBrush
            }
        });
        return body;
    }
}
