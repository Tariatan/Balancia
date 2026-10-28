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

using static Balancia.Desktop.FormControls;
using static Balancia.Desktop.InputParsing;
using static Balancia.Desktop.Localization.UiText;

namespace Balancia.Desktop;

public partial class MainWindow
{
    private void InitializeTransactionsPanel()
    {
        OverviewPane.TransactionsCard.AddRequested += async () => await EditTransaction(null);
        OverviewPane.TransactionsCard.EditRequested += async entry => await EditTransaction(entry);
        OverviewPane.TransactionsCard.RemoveRequested += async () =>
        {
            if (OverviewPane.TransactionsCard.SelectedEntry is { } entry)
            {
                await RemoveTransaction(entry);
            }
        };
    }

    private async Task EditTransaction(LedgerEntry? entry)
    {
        var existing = entry?.Draft;
        if (TransactionFormRules.AvailableAccounts(OverviewPane.Data!.Snapshot.Accounts, existing).Count == 0)
        {
            SetStatus("Add an active account before entering transactions.");
            return;
        }

        var form = new TransactionForm(new TransactionFormInputs(existing, Today, OverviewPane.Data.Snapshot.Accounts, OverviewPane.Data.Snapshot.Categories,
            settings.DefaultAccountId, lastAccountId, await Task.Run(() => store.ReadRecentDescriptions()),
            await Task.Run(() => store.ReadRegularCategoryPaths()), () => Task.Run(() => store.ReadRegularCategoryPaths())));

        await dialogs.ShowAsync(entry is null ? "Add transaction" : "Edit transaction", form.Fields,
            () =>
            {
                var (draft, categoryPath) = form.Read();
                lastAccountId = draft.AccountId;
                return () => store.SaveTransactionWithCategoryPath(entry?.Id, draft, categoryPath);
            },
            new DialogOptions(DialogSize.Tall, InitialFocus: form.InitialFocus, OnSaveAndContinue: form.ContinueAfterSave, Validate: form.Validate));
    }

    private Task RemoveTransaction(LedgerEntry entry) => dialogs.ShowAsync("Remove transaction",
        [
            TransactionRows.Build(new TransactionsHit(entry, null), "(No description)"),
            Text("Remove this transaction? For a transfer, both account movements will be removed together.")
        ],
        () => () => store.DeleteTransaction(entry.Id), DialogOptions.Confirmation("Remove", size: DialogSize.Wide));
}
