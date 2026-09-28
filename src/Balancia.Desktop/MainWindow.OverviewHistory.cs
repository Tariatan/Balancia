using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Balancia.Storage;

using static Balancia.Desktop.Localization.UiText;

namespace Balancia.Desktop;

public partial class MainWindow
{
    private const int HistoryPageSize = 100;
    private HistoryPage? overviewHistory;
    private int overviewOffset;

    private void InitializeHistoryPanel()
    {
        overviewHistoryList.ItemTemplate = new FuncDataTemplate<HistoryItem>((item, _) => HistoryRow(item.Hit), true);
        overviewHistoryList.DoubleTapped += async (_, _) =>
        {
            if (overviewHistoryList.SelectedItem is HistoryItem item)
            {
                await EditTransaction(item.Hit.Entry);
            }
        };
        overviewHistoryAddButton.Click += async (_, _) => await EditTransaction(null);
        overviewHistoryRemoveButton.Click += async (_, _) =>
        {
            if (overviewHistoryList.SelectedItem is HistoryItem item)
            {
                await RemoveTransaction(item.Hit.Entry);
            }
        };
        overviewPreviousPage.Click += async (_, _) =>
        {
            overviewOffset = Math.Max(0, overviewOffset - HistoryPageSize);
            await RequestOverviewFilterRefresh();
        };
        overviewNextPage.Click += async (_, _) =>
        {
            overviewOffset += HistoryPageSize;
            await RequestOverviewFilterRefresh();
        };
    }

    private void LocalizeHistoryPanel()
    {
        overviewHistoryColumnDate.Text = Get("Date");
        overviewHistoryColumnAmount.Text = Get("Amount");
        overviewHistoryColumnCategory.Text = Get("Category");
        overviewHistoryColumnAccount.Text = Get("Account");
        overviewHistoryColumnDescription.Text = Get("Description");
        overviewHistoryEmpty.Text = Get("No matching transactions.");
        ToolTip.SetTip(overviewHistoryAddButton, Get("Add transaction"));
        ToolTip.SetTip(overviewHistoryRemoveButton, Get("Delete selected transaction"));
    }
}
