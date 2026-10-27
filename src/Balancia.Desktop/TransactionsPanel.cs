using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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

    private static readonly IBrush SuggestionSelectedBrush = Brush.Parse("#3989A7");
    private static readonly IBrush SuggestionTopMatchBrush = Brush.Parse("#52636C");

    private void InitializeTransactionsPanel()
    {
        TransactionsCard.AddRequested += async () => await EditTransaction(null);
        TransactionsCard.EditRequested += async entry => await EditTransaction(entry);
        TransactionsCard.RemoveRequested += async () =>
        {
            if (TransactionsCard.SelectedEntry is { } entry)
            {
                await RemoveTransaction(entry);
            }
        };
        TransactionsCard.PreviousPageRequested += async () => await ChangeTransactionsPage(-TransactionsPageSize);
        TransactionsCard.NextPageRequested += async () => await ChangeTransactionsPage(TransactionsPageSize);
    }

    private async Task ChangeTransactionsPage(int delta)
    {
        overviewState.ChangePage(delta);
        await RequestOverviewFilterRefresh();
        TransactionsCard.ScrollToTop();
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

        var startAccount = TransactionFormRules.DefaultAccount([.. accounts.Select(c => c.Value)], existing?.AccountId, settings.DefaultAccountId, lastAccountId);
        var account = ChoiceBox(accounts, accounts.First(c => c.Value == startAccount));

        var destination = ChoiceBox(accounts, accounts.FirstOrDefault(c => c.Value.Id == existing?.DestinationId));

        var availableCategories = TransactionFormRules.AvailableCategories(overview!.Snapshot.Categories, existing?.CategoryId);

        var category = new TextBox
        {
            Text = overview!.Snapshot.Categories.FirstOrDefault(c => c.Id == existing?.CategoryId)?.Path ?? "",
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
                var draft = TransactionFormRules.ToDraft(type, ParseDate(date), description.Text ?? "", ParseMoney(amount),
                    SelectedValue<Account>(account).Id, (destination.SelectedItem as Choice<Account>)?.Value.Id, memo.Text ?? "");
                var categoryPath = TransactionFormRules.CategoryPathFor(type, category.Text, availableCategories);
                lastAccountId = draft.AccountId;
                return () => store.SaveTransactionWithCategoryPath(entry?.Id, draft, categoryPath);
            },
            new DialogOptions(DialogSize.Tall, InitialFocus: category, OnSaveAndContinue: continueAfterSave, Validate: ValidateFields));
        return;

        string? ValidateFields()
        {
            var type = SelectedValue<TransactionKind>(kind);
            return TransactionFormRules.Validate(type, ParseDate(date), SelectedValue<Account>(account),
                (destination.SelectedItem as Choice<Account>)?.Value, category.Text, availableCategories, overview!.Snapshot.Categories);
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
        .. TransactionFormRules.AvailableAccounts(overview!.Snapshot.Accounts, existing)
            .Select(a => new Choice<Account>(a, ArchivedLabel.Of(a.Name, a.Archived)))
    ];

    private static ComboBox ChoiceBox<T>(Choice<T>[] choices, Choice<T>? selected) => new()
    {
        ItemsSource = choices,
        SelectedItem = selected,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };

    private static T SelectedValue<T>(ComboBox box) => ((Choice<T>)box.SelectedItem!).Value;

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
            TransactionRows.Build(new TransactionsHit(entry, null), "(No description)"),
            Text("Remove this transaction? For a transfer, both account movements will be removed together.")
        ],
        () => () => store.DeleteTransaction(entry.Id), DialogOptions.Confirmation("Remove", size: DialogSize.Wide));

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
