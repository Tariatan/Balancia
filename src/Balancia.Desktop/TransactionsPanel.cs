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
        var descriptionInput = new DescriptionSuggestBox(description, descriptionHistory);

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

        var categoryInput = new CategorySuggestBox(category,
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
                categoryInput.Reset(regularCategoryPaths);
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

    private Task RemoveTransaction(LedgerEntry entry) => EditDialog("Remove transaction",
        [
            TransactionRows.Build(new TransactionsHit(entry, null), "(No description)"),
            Text("Remove this transaction? For a transfer, both account movements will be removed together.")
        ],
        () => () => store.DeleteTransaction(entry.Id), DialogOptions.Confirmation("Remove", size: DialogSize.Wide));
}
