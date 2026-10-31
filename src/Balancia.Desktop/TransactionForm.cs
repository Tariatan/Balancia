using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using Balancia.Core;

using static Balancia.Desktop.FormControls;
using static Balancia.Desktop.InputParsing;
using static Balancia.Desktop.Localization.UiText;

namespace Balancia.Desktop;

/// <summary>What the add and edit transaction form needs to know, read before it is built; nothing here reaches back into the window or the store.</summary>
/// <param name="Existing">The transaction being edited, or null when adding.</param>
/// <param name="Accounts">Every account; the form offers the ones that fit.</param>
/// <param name="Categories">Every category; the form offers the ones that fit.</param>
/// <param name="RecentDescriptions">Earlier descriptions for the description suggestions.</param>
/// <param name="RegularCategoryPaths">The category paths the user uses regularly, for ranking the category suggestions.</param>
/// <param name="ReadRegularCategoryPaths">Reads the regular category paths again, after a transaction was saved with "Add another".</param>
internal sealed record TransactionFormInputs(
    TransactionDraft? Existing,
    DateOnly Today,
    IReadOnlyList<Account> Accounts,
    IReadOnlyList<Category> Categories,
    string? DefaultAccountId,
    string? LastAccountId,
    IEnumerable<string> RecentDescriptions,
    IReadOnlyList<string> RegularCategoryPaths,
    Func<Task<IReadOnlyList<string>>> ReadRegularCategoryPaths);

/// <summary>The fields of the add and edit transaction dialog and what they mean: what they start with, when they are valid, what they save, and how "Add another" resets them.</summary>
internal sealed class TransactionForm
{
    private readonly IReadOnlyList<Category> allCategories;
    private readonly TransactionDraft? existing;
    private readonly IReadOnlyList<Category> availableCategories;
    private readonly CalendarDatePicker date;
    private readonly TextBox description;
    private readonly DescriptionSuggestBox descriptionInput;
    private readonly TextBox amount;
    private readonly ComboBox account;
    private readonly ComboBox destination;
    private readonly TextBox category;
    private readonly CategorySuggestBox categoryInput;
    private readonly TextBox memo;
    private readonly ComboBox kind;
    private readonly StackPanel destinationField;
    private readonly StackPanel categoryField;

    public TransactionForm(TransactionFormInputs inputs)
    {
        existing = inputs.Existing;
        allCategories = inputs.Categories;
        var accounts = TransactionFormRules.AvailableAccounts(inputs.Accounts, existing)
            .Select(a => new Choice<Account>(a, ArchivedLabel.Of(a.Name, a.Archived)))
            .ToArray();

        date = DateInput(existing?.Date ?? inputs.Today);
        description = Input(existing?.Description ?? "");
        descriptionInput = new DescriptionSuggestBox(description, inputs.RecentDescriptions);

        amount = new AmountTextBox
        {
            Text = existing?.Amount.Francs.ToString("0.00", Culture) ?? string.Empty,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var startAccount = TransactionFormRules.DefaultAccount([.. accounts.Select(c => c.Value)], existing?.AccountId, inputs.DefaultAccountId, inputs.LastAccountId);
        account = ChoiceBox(accounts, accounts.First(c => c.Value == startAccount));

        destination = ChoiceBox(accounts, accounts.FirstOrDefault(c => c.Value.Id == existing?.DestinationId));

        availableCategories = TransactionFormRules.AvailableCategories(allCategories, existing?.CategoryId);

        category = new TextBox
        {
            Text = allCategories.FirstOrDefault(c => c.Id == existing?.CategoryId)?.Path ?? "",
            PlaceholderText = Get("Type or select a category"),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        categoryInput = new CategorySuggestBox(category, [.. availableCategories.Where(c => c.ParentId is not null).Select(c => c.Path)], inputs.RegularCategoryPaths);
        category.LostFocus += (_, _) => Dispatcher.UIThread.Post(ShowCategoryPath);
        memo = Input(existing?.Memo ?? "");
        destinationField = Field("Destination account", destination);
        categoryField = Field("Category / subcategory", categoryInput);

        var kinds = Enum.GetValues<TransactionKind>()
            .Select(value => new Choice<TransactionKind>(value, Get(value.ToString())))
            .ToArray();
        kind = ChoiceBox(kinds, kinds.Single(choice => choice.Value == (existing?.Kind ?? TransactionKind.Expense)));
        kind.SelectionChanged += (_, _) => UpdateFields();
        UpdateFields();

        Fields =
        [
            Field("Type", kind),
            Field("Date", date), categoryField,
            Field("Amount (positive)", amount),
            Field("Account", account), destinationField,
            Field("Description", descriptionInput),
            Field("Notes", memo)
        ];

        if (existing is null)
        {
            ContinueAfterSave = async () =>
            {
                descriptionInput.Remember(description.Text ?? "");
                var regularCategoryPaths = await inputs.ReadRegularCategoryPaths();
                description.Text = "";
                amount.Text = string.Empty;
                memo.Text = "";
                categoryInput.Reset(regularCategoryPaths);
            };
        }
    }

    /// <summary>The labelled fields, in the order the dialog shows them.</summary>
    public Control[] Fields { get; }

    /// <summary>The field the dialog starts in.</summary>
    public InputElement InitialFocus => category;

    /// <summary>Resets the form for the next entry after one was saved; null when editing, because an edit has no "Add another".</summary>
    public Func<Task>? ContinueAfterSave { get; }

    /// <summary>Why the form cannot be saved yet, or null when it can. A missing date throws <see cref="FormatException"/>.</summary>
    public string? Validate() => TransactionFormRules.Validate(SelectedValue<TransactionKind>(kind), ParseDate(date),
        SelectedValue<Account>(account), (destination.SelectedItem as Choice<Account>)?.Value, category.Text, allCategories, existing);

    /// <summary>
    /// The entered values: the draft to save and the typed category path (none for a transfer).
    /// An unreadable date or amount throws <see cref="FormatException"/>.
    /// </summary>
    public (TransactionDraft Draft, string? CategoryPath) Read()
    {
        var type = SelectedValue<TransactionKind>(kind);
        var enteredAmount = decimal.Parse(amount.Text?.Replace(',', '.') ?? string.Empty, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
        var draft = TransactionFormRules.ToDraft(type, ParseDate(date), description.Text ?? "", Money.FromFrancs(enteredAmount),
            SelectedValue<Account>(account).Id, (destination.SelectedItem as Choice<Account>)?.Value.Id, memo.Text ?? "");
        return (draft, TransactionFormRules.CategoryPathFor(type, category.Text, availableCategories));
    }

    /// <summary>Rewrites the typed category in the one spelling the ledger shows, such as <c>food/lunch</c> as <c>Food / Lunch</c>, whether or not it names a category.</summary>
    private void ShowCategoryPath()
    {
        if (TransactionFormRules.ReadCategoryPath(category.Text, availableCategories) is { } path && path != category.Text)
        {
            categoryInput.Settle(path);
        }
    }

    private void UpdateFields()
    {
        var transfer = (kind.SelectedItem as Choice<TransactionKind>)?.Value == TransactionKind.Transfer;
        destinationField.IsVisible = transfer;
        categoryField.IsVisible = !transfer;
    }
}
