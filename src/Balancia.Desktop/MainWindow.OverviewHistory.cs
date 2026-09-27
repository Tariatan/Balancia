using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Balancia.Storage;

using static Balancia.Desktop.Localization.UiText;

namespace Balancia.Desktop;

public partial class MainWindow
{
    private const int HistoryPageSize = 100;
    private HistoryPage? overviewHistory;
    private int overviewOffset;

    private TextBlock? overviewHistoryHeading;
    private ListBox? overviewHistoryList;
    private TextBlock? overviewHistoryEmpty;
    private Button? overviewPreviousPage;
    private Button? overviewNextPage;

    private Border HistoryPanel()
    {
        var body = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
            RowSpacing = 2
        };
        var historyList = HistoryList(overviewHistory?.Hits ?? []);
        overviewHistoryList = historyList;
        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(0, 0, 0, 10)
        };
        var total = overviewHistory?.TotalCount ?? 0;
        var first = total == 0 ? 0 : overviewOffset + 1;
        var last = overviewOffset + (overviewHistory?.Hits.Count ?? 0);
        overviewHistoryHeading = Heading($"{Get("Transactions")} · {first:N0}-{last:N0} / {total:N0}", 13);
        header.Children.Add(overviewHistoryHeading);
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 3
        };
        var add = ActionButton("+", () => EditTransaction(null));
        add.Width = 30;
        add.Padding = new Thickness(0);
        add.FontSize = 18;
        add.HorizontalContentAlignment = HorizontalAlignment.Center;

        var remove = ActionButton("🗑", async () =>
        {
            if (historyList.SelectedItem is HistoryItem item)
            {
                await RemoveTransaction(item.Hit.Entry);
            }
        });
        remove.Width = 30;
        remove.Padding = new Thickness(0);
        remove.FontSize = 18;
        remove.HorizontalContentAlignment = HorizontalAlignment.Center;

        ToolTip.SetTip(add, Get("Add transaction"));
        ToolTip.SetTip(remove, Get("Delete selected transaction"));
        actions.Children.Add(add);
        actions.Children.Add(remove);
        AddColumn(header, actions, 1);
        AddRow(body, header, 0);
        AddRow(body, HistoryRow("Date", "Description", "Category", "Account", "Amount", true), 1);

        var historyContent = new Grid();
        overviewHistoryEmpty = QuietText("No matching transactions.", 13);
        overviewHistoryEmpty.IsVisible = total == 0;
        overviewHistoryEmpty.VerticalAlignment = VerticalAlignment.Top;
        overviewHistoryEmpty.Margin = new Thickness(0, 6, 0, 0);
        historyContent.Children.Add(overviewHistoryEmpty);
        historyList.DoubleTapped += async (_, _) =>
        {
            if (historyList.SelectedItem is HistoryItem item)
            {
                await EditTransaction(item.Hit.Entry);
            }
        };
        historyContent.Children.Add(historyList);
        AddRow(body, historyContent, 2);

        var previous = ActionButton("Previous page", async () =>
        {
            overviewOffset = Math.Max(0, overviewOffset - HistoryPageSize);
            await RequestOverviewFilterRefresh();
        });
        overviewPreviousPage = previous;
        previous.IsEnabled = overviewOffset > 0;
        var next = ActionButton("Next page", async () =>
        {
            overviewOffset += HistoryPageSize;
            await RequestOverviewFilterRefresh();
        });
        overviewNextPage = next;
        next.IsEnabled = overviewHistory is { } currentPage && overviewOffset + currentPage.Hits.Count < currentPage.TotalCount;
        AddRow(body, Row(previous, next), 3);

        var panel = Panel(body);
        panel.Padding = new Thickness(15, 15, 15, 5);
        return panel;
    }
}
