using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Balancia.Storage;

using static Balancia.Desktop.Localization.UiText;

namespace Balancia.Desktop;

public partial class MainWindow
{
    private const int TransactionsPageSize = 100;
    private TransactionsPage? transactions;
    private int offset;

    private void InitializeTransactionsPanel()
    {
        TransactionsList.ItemTemplate = new FuncDataTemplate<TransactionItem>((item, _) => TransactionRow(item.Hit), true);
        TransactionsList.DoubleTapped += async (_, _) =>
        {
            if (TransactionsList.SelectedItem is TransactionItem item)
            {
                await EditTransaction(item.Hit.Entry);
            }
        };
        TransactionsAddButton.Click += async (_, _) => await EditTransaction(null);
        TransactionsRemoveButton.Click += async (_, _) =>
        {
            if (TransactionsList.SelectedItem is TransactionItem item)
            {
                await RemoveTransaction(item.Hit.Entry);
            }
        };
        TransactionsPreviousPage.Click += async (_, _) =>
        {
            offset = Math.Max(0, offset - TransactionsPageSize);
            await RequestOverviewFilterRefresh();
        };
        TransactionsNextPage.Click += async (_, _) =>
        {
            offset += TransactionsPageSize;
            await RequestOverviewFilterRefresh();
        };
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
}
