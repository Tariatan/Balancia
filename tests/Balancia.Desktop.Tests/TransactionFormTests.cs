using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Balancia.Core;
using Xunit;

using static Balancia.Desktop.Localization.UiText;

namespace Balancia.Desktop.Tests;

public sealed class TransactionFormTests
{
    [AvaloniaTheory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Food")]
    public void Validate_WithoutSubcategory_RejectsIncomeAndExpense(string? text)
    {
        // Arrange
        var form = new TransactionForm(Inputs());
        TextBoxOf(form, "Category / subcategory").Text = text;

        // Act and Assert
        Assert.Equal("Choose an existing subcategory.", form.Validate());
        Select(form, "Type", Get("Income"));
        Assert.Equal("Choose an existing subcategory.", form.Validate());
    }

    [AvaloniaFact]
    public void Suggestions_ParentQuery_OffersOnlyMatchingSubcategories()
    {
        // Arrange
        var form = new TransactionForm(Inputs(regular: ["Food", "Food / Lunch"]));
        var input = Control<CategorySuggestBox>(form, "Category / subcategory");

        // Act
        TextBoxOf(form, "Category / subcategory").Text = "food";
        Dispatcher.UIThread.RunJobs();

        // Assert
        Assert.Equal(["Food / Lunch"], input.Suggestions.Select(suggestion => suggestion.Path));
        Assert.Equal(CategorySuggestionSection.Regular, input.Suggestions[0].Section);
    }

    private static readonly DateOnly Today = new(2026, 10, 13);
    private static readonly DateOnly Opened = new(2026, 1, 1);

    private static readonly Account Everyday = new("a", "Everyday", Opened, Money.Zero, false, Money.Zero);
    private static readonly Account Savings = new("b", "Savings", Opened, Money.Zero, false, Money.Zero);
    private static readonly Account Closed = new("c", "Closed", Opened, Money.Zero, true, Money.Zero);
    private static readonly Account Late = new("d", "Late", new DateOnly(2026, 6, 1), Money.Zero, false, Money.Zero);

    private static readonly Category Food = new("food", "Food", null, "Food", false);
    private static readonly Category Lunch = new("lunch", "Lunch", "food", "Food / Lunch", false);
    private static readonly Category Old = new("old", "Old", "food", "Food / Old", true);

    private static TransactionFormInputs Inputs(TransactionDraft? existing = null, string? defaultAccountId = null, string? lastAccountId = null,
        IEnumerable<string>? recent = null, IReadOnlyList<string>? regular = null, Func<Task<IReadOnlyList<string>>>? reread = null,
        IReadOnlyList<Account>? accounts = null) =>
        new(existing, Today, accounts ?? [Everyday, Savings, Closed, Late], [Food, Lunch, Old], defaultAccountId, lastAccountId,
            recent ?? [], regular ?? [], reread ?? (() => Task.FromResult<IReadOnlyList<string>>([])));

    private static TransactionDraft Draft(TransactionKind kind = TransactionKind.Expense, string accountId = "a", string? destinationId = null,
        string? categoryId = null, decimal francs = 12.5m, string description = "Lunch", string memo = "note", DateOnly? date = null) =>
        new(kind, date ?? Today, description, Money.FromFrancs(francs), accountId, destinationId, categoryId, memo);

    private static StackPanel FieldOf(TransactionForm form, string label) =>
        form.Fields.OfType<StackPanel>().Single(field => AutomationProperties.GetName(field.Children[1]) == Get(label));

    private static T Control<T>(TransactionForm form, string label) where T : Control => (T)FieldOf(form, label).Children[1];

    private static TextBox TextBoxOf(TransactionForm form, string label)
    {
        var control = Control<Control>(form, label);
        return control as TextBox ?? control.GetLogicalDescendants().OfType<TextBox>().First();
    }

    private static string TextOf(TransactionForm form, string label) => TextBoxOf(form, label).Text ?? "";

    private static void Select(TransactionForm form, string label, string choiceLabel)
    {
        var combo = Control<ComboBox>(form, label);
        combo.SelectedItem = combo.ItemsSource!.Cast<object>().Single(item => item.ToString() == choiceLabel);
    }

    private static string? Selected(TransactionForm form, string label) => Control<ComboBox>(form, label).SelectedItem?.ToString();

    [AvaloniaFact]
    public void Fields_AreInTheDialogOrderUnderTheirLabels()
    {
        // Act
        var form = new TransactionForm(Inputs());

        // Assert
        var labels = form.Fields.OfType<StackPanel>().Select(field => ((TextBlock)field.Children[0]).Text);
        Assert.Equal(new[]
        {
            "Type", "Date", "Category / subcategory", "Amount (positive)", "Account", "Destination account", "Description", "Notes",
        }.Select(Get), labels);
    }

    [AvaloniaFact]
    public void NewForm_StartsAsAnEmptyExpenseDatedToday()
    {
        // Act
        var form = new TransactionForm(Inputs());

        // Assert
        Assert.Equal(Get("Expense"), Selected(form, "Type"));
        Assert.Equal("2026-10-13", form.Fields.OfType<StackPanel>().Select(f => f.Children[1]).OfType<CalendarDatePicker>().Single().SelectedDate!.Value.ToString("yyyy-MM-dd"));
        Assert.Equal("", TextOf(form, "Amount (positive)"));
        Assert.Equal("", TextOf(form, "Description"));
        Assert.Equal("", TextOf(form, "Category / subcategory"));
        Assert.Equal("", TextOf(form, "Notes"));
        Assert.Null(Control<ComboBox>(form, "Destination account").SelectedItem);
    }

    [AvaloniaFact]
    public void ExistingTransaction_FillsEveryField()
    {
        // Arrange
        var existing = Draft(TransactionKind.Income, "b", categoryId: "lunch", francs: 99.5m, description: "Refund", memo: "from Anna", date: new DateOnly(2026, 9, 1));

        // Act
        var form = new TransactionForm(Inputs(existing));

        // Assert
        Assert.Equal(Get("Income"), Selected(form, "Type"));
        Assert.Equal("Savings", Selected(form, "Account"));
        Assert.Equal(99.5m.ToString("0.00", Culture), TextOf(form, "Amount (positive)"));
        Assert.Equal("Refund", TextOf(form, "Description"));
        Assert.Equal("from Anna", TextOf(form, "Notes"));
        Assert.Equal("Food / Lunch", TextOf(form, "Category / subcategory"));
        Assert.Equal(new DateTime(2026, 9, 1), Control<CalendarDatePicker>(form, "Date").SelectedDate);
    }

    [AvaloniaFact]
    public void Kind_Transfer_ShowsTheDestinationAndHidesTheCategoryAndBack()
    {
        // Arrange
        var form = new TransactionForm(Inputs());
        var destination = FieldOf(form, "Destination account");
        var category = FieldOf(form, "Category / subcategory");
        var expense = (destination.IsVisible, category.IsVisible);

        // Act
        Select(form, "Type", Get("Transfer"));
        var transfer = (destination.IsVisible, category.IsVisible);
        Select(form, "Type", Get("Income"));

        // Assert
        Assert.Equal((false, true), expense);
        Assert.Equal((true, false), transfer);
        Assert.Equal((false, true), (destination.IsVisible, category.IsVisible));
    }

    [AvaloniaFact]
    public void ExistingTransferOfAnotherKind_StartsWithItsOwnFieldsVisible()
    {
        // Act
        var form = new TransactionForm(Inputs(Draft(TransactionKind.Transfer, "a", destinationId: "b")));

        // Assert
        Assert.True(FieldOf(form, "Destination account").IsVisible);
        Assert.False(FieldOf(form, "Category / subcategory").IsVisible);
        Assert.Equal("Savings", Selected(form, "Destination account"));
    }

    [AvaloniaFact]
    public void Accounts_OfferedAreTheActiveOnesAndAnArchivedOneTheEditedEntryUses()
    {
        // Act
        var adding = new TransactionForm(Inputs());
        var editing = new TransactionForm(Inputs(Draft(accountId: "c")));

        // Assert
        Assert.Equal(["Everyday", "Savings", "Late"], Control<ComboBox>(adding, "Account").ItemsSource!.Cast<object>().Select(i => i.ToString()));
        Assert.Equal(["Everyday", "Savings", $"Closed{Get(" (archived)")}", "Late"],
            Control<ComboBox>(editing, "Account").ItemsSource!.Cast<object>().Select(i => i.ToString()));
        Assert.Equal($"Closed{Get(" (archived)")}", Selected(editing, "Account"));
    }

    [AvaloniaTheory]
    [InlineData(null, null, "Everyday")]
    [InlineData("b", null, "Savings")]
    [InlineData(null, "b", "Savings")]
    [InlineData("d", "b", "Late")]
    [InlineData("zzz", "b", "Everyday")]
    public void Account_StartsOnTheConfiguredDefaultThenTheLastUsedElseTheFirst(string? defaultId, string? lastId, string expected)
    {
        // Act
        var form = new TransactionForm(Inputs(defaultAccountId: defaultId, lastAccountId: lastId));

        // Assert
        Assert.Equal(expected, Selected(form, "Account"));
    }

    [AvaloniaFact]
    public void InitialFocus_IsTheCategoryBox()
    {
        // Act
        var form = new TransactionForm(Inputs());

        // Assert
        Assert.Same(TextBoxOf(form, "Category / subcategory"), form.InitialFocus);
    }

    [AvaloniaFact]
    public void Read_Expense_ReturnsTheEnteredValuesAndTheCategoryPathInItsStoredSpelling()
    {
        // Arrange
        var form = new TransactionForm(Inputs());
        TextBoxOf(form, "Amount (positive)").Text = 12.5m.ToString("0.00", Culture);
        TextBoxOf(form, "Description").Text = "Lunch";
        TextBoxOf(form, "Notes").Text = "with Anna";
        TextBoxOf(form, "Category / subcategory").Text = " food / lunch ";

        // Act
        var (draft, categoryPath) = form.Read();

        // Assert
        Assert.Equal(new TransactionDraft(TransactionKind.Expense, Today, "Lunch", Money.FromFrancs(12.5m), "a", Memo: "with Anna"), draft);
        Assert.Equal("Food / Lunch", categoryPath);
    }

    [AvaloniaTheory]
    [InlineData("en", "5.50")]
    [InlineData("de", "5,50")]
    public void Read_DecimalAmount_UsesSelectedLanguage(string language, string text)
    {
        // Arrange
        var previousLanguage = Culture.TwoLetterISOLanguageName;
        SetLanguage(language);
        try
        {
            var form = new TransactionForm(Inputs());
            TextBoxOf(form, "Amount (positive)").Text = text;

            // Act
            var (draft, _) = form.Read();

            // Assert
            Assert.Equal(Money.FromFrancs(5.5m), draft.Amount);
            Assert.Equal(text, TextOf(form, "Amount (positive)"));
        }
        finally
        {
            SetLanguage(previousLanguage);
        }
    }

    [AvaloniaTheory]
    [InlineData("abc")]
    [InlineData("2+3")]
    [InlineData("1.2.3")]
    [InlineData("-12")]
    [InlineData("")]
    public void Read_InvalidAmountWhileEditing_RejectsInsteadOfSavingPreviousValue(string text)
    {
        // Arrange
        var form = new TransactionForm(Inputs(Draft()));
        TextBoxOf(form, "Amount (positive)").Text = text;

        // Act and Assert
        Assert.Throws<FormatException>(() => form.Read());
    }

    [AvaloniaFact]
    public void Read_FractionalCentime_RejectsWithoutRounding()
    {
        // Arrange
        var form = new TransactionForm(Inputs());
        TextBoxOf(form, "Amount (positive)").Text = 1.234m.ToString(Culture);

        // Act and Assert
        Assert.Throws<ArgumentException>(() => form.Read());
    }

    [AvaloniaFact]
    public void Read_Transfer_KeepsTheDestinationAndHasNoCategory()
    {
        // Arrange
        var form = new TransactionForm(Inputs());
        Select(form, "Type", Get("Transfer"));
        Select(form, "Destination account", "Savings");
        TextBoxOf(form, "Amount (positive)").Text = "40";
        TextBoxOf(form, "Category / subcategory").Text = "Food";

        // Act
        var (draft, categoryPath) = form.Read();

        // Assert
        Assert.Equal(TransactionKind.Transfer, draft.Kind);
        Assert.Equal("b", draft.DestinationId);
        Assert.Null(categoryPath);
    }

    [AvaloniaFact]
    public void Read_NotATransfer_DropsAChosenDestination()
    {
        // Arrange
        var form = new TransactionForm(Inputs(Draft(TransactionKind.Transfer, "a", destinationId: "b")));
        Select(form, "Type", Get("Expense"));

        // Act
        var (draft, _) = form.Read();

        // Assert
        Assert.Null(draft.DestinationId);
    }

    [AvaloniaTheory]
    [InlineData("")]
    [InlineData("abc")]
    public void Read_UnreadableAmount_ThrowsFormatException(string text)
    {
        // Arrange
        var form = new TransactionForm(Inputs());
        TextBoxOf(form, "Amount (positive)").Text = text;

        // Act
        void Act() => form.Read();

        // Assert
        Assert.Throws<FormatException>(Act);
    }

    [AvaloniaFact]
    public void ReadAndValidate_NoDateSelected_ThrowFormatException()
    {
        // Arrange
        var form = new TransactionForm(Inputs());
        TextBoxOf(form, "Amount (positive)").Text = "5";
        Control<CalendarDatePicker>(form, "Date").SelectedDate = null;

        // Act and Assert
        Assert.Throws<FormatException>(() => form.Read());
        Assert.Throws<FormatException>(() => form.Validate());
    }

    [AvaloniaFact]
    public void Validate_ValidForm_IsNull()
    {
        // Arrange
        var form = new TransactionForm(Inputs());
        TextBoxOf(form, "Category / subcategory").Text = "Food / Lunch";

        // Act and Assert
        Assert.Null(form.Validate());
    }

    [AvaloniaFact]
    public void Validate_DateBeforeTheAccountOpened_ExplainsIt()
    {
        // Arrange
        var form = new TransactionForm(Inputs());
        Select(form, "Account", "Late");
        Control<CalendarDatePicker>(form, "Date").SelectedDate = new DateTime(2026, 5, 1);

        // Act
        var error = form.Validate();

        // Assert
        Assert.Equal("Transaction date precedes the account's opening date.", error);
    }

    [AvaloniaFact]
    public void Validate_UnknownCategory_ExplainsIt()
    {
        // Arrange
        var form = new TransactionForm(Inputs());
        TextBoxOf(form, "Category / subcategory").Text = "Travel";

        // Act and Assert
        Assert.Equal("Choose an existing category path. No category \"Travel\".", form.Validate());
    }

    [AvaloniaTheory]
    [InlineData("food/lunch", "Food / Lunch")]
    [InlineData("Food /Lunch", "Food / Lunch")]
    [InlineData("  FOOD  /  lunch ", "Food / Lunch")]
    [InlineData("Food", "Food")]
    [InlineData("travel/ Dinner", "travel / Dinner")] // an unknown path is tidied too
    public void LeavingTheCategory_ShowsItInTheSpellingTheLedgerUses(string typed, string expected)
    {
        // Arrange
        var form = new TransactionForm(Inputs());
        var box = TextBoxOf(form, "Category / subcategory");
        box.Text = typed;

        // Act
        box.RaiseEvent(new FocusChangedEventArgs(InputElement.LostFocusEvent));
        Dispatcher.UIThread.RunJobs();

        // Assert
        Assert.Equal(expected, box.Text);
    }

    [AvaloniaTheory]
    [InlineData("food/lunch")]
    [InlineData("Food /Lunch")]
    public void Read_CategoryTypedWithAnotherSpelling_ReturnsTheLedgerSpelling(string typed)
    {
        // Arrange
        var form = new TransactionForm(Inputs());
        TextBoxOf(form, "Category / subcategory").Text = typed;
        TextBoxOf(form, "Amount (positive)").Text = "5";

        // Act
        var (_, path) = form.Read();

        // Assert
        Assert.Equal("Food / Lunch", path);
    }

    [AvaloniaFact]
    public void Validate_ArchivedCategory_AsksToRestoreItUnlessTheEditedEntryUsesIt()
    {
        // Arrange
        var adding = new TransactionForm(Inputs());
        TextBoxOf(adding, "Category / subcategory").Text = "Food / Old";
        var editing = new TransactionForm(Inputs(Draft(categoryId: "old")));

        // Act and Assert
        Assert.Equal("Restore the archived category before using it for another transaction.", adding.Validate());
        Assert.Null(editing.Validate());
    }

    [AvaloniaFact]
    public void Validate_Transfer_NeedsNoCategory()
    {
        // Arrange
        var form = new TransactionForm(Inputs());
        Select(form, "Type", Get("Transfer"));
        TextBoxOf(form, "Category / subcategory").Text = "Nonsense";

        // Act and Assert
        Assert.Null(form.Validate());
    }

    [AvaloniaFact]
    public void ContinueAfterSave_WhenEditing_IsNull()
    {
        // Act
        var form = new TransactionForm(Inputs(Draft()));

        // Assert
        Assert.Null(form.ContinueAfterSave);
    }

    [AvaloniaFact]
    public async Task ContinueAfterSave_WhenAdding_ClearsTheEntryFieldsAndKeepsTheRest()
    {
        // Arrange
        var form = new TransactionForm(Inputs());
        Select(form, "Type", Get("Income"));
        Select(form, "Account", "Savings");
        TextBoxOf(form, "Amount (positive)").Text = "5";
        TextBoxOf(form, "Description").Text = "Coffee";
        TextBoxOf(form, "Notes").Text = "note";
        TextBoxOf(form, "Category / subcategory").Text = "Food";

        // Act
        await form.ContinueAfterSave!();

        // Assert
        Assert.Equal(["", "", "", ""], new[]
        {
            TextOf(form, "Amount (positive)"), TextOf(form, "Description"), TextOf(form, "Notes"), TextOf(form, "Category / subcategory"),
        });
        Assert.Equal(Get("Income"), Selected(form, "Type"));
        Assert.Equal("Savings", Selected(form, "Account"));
    }

    [AvaloniaFact]
    public async Task ContinueAfterSave_WhenAdding_RereadsTheRegularCategoriesAndOffersTheSavedDescription()
    {
        // Arrange
        var reads = 0;
        var form = new TransactionForm(Inputs(recent: ["Rent"], regular: [], reread: () =>
        {
            reads++;
            return Task.FromResult<IReadOnlyList<string>>(["Food / Lunch"]);
        }));
        TextBoxOf(form, "Description").Text = "Coffee";

        // Act
        await form.ContinueAfterSave!();
        TextBoxOf(form, "Description").Text = "cof";
        Dispatcher.UIThread.RunJobs();

        // Assert
        Assert.Equal(1, reads);
        var suggestions = form.Fields.SelectMany(f => f.GetLogicalDescendants()).OfType<DescriptionSuggestBox>().Single();
        Assert.Equal(["Coffee"], suggestions.Matches);
        var categories = form.Fields.SelectMany(f => f.GetLogicalDescendants()).OfType<CategorySuggestBox>().Single();
        TextBoxOf(form, "Category / subcategory").Text = "lunch";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Food / Lunch", categories.Suggestions[0].Path);
        Assert.Equal(CategorySuggestionSection.Regular, categories.Suggestions[0].Section);
    }
}
