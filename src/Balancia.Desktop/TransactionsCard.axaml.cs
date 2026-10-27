using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Media;
using Avalonia.Styling;
using Balancia.Core;
using Balancia.Storage;

using static Balancia.Desktop.InputParsing;
using static Balancia.Desktop.Localization.UiText;

namespace Balancia.Desktop;

/// <summary>
/// The transactions card: one page of transactions shaded by day, with the range heading and the previous and next page buttons.
/// It draws what it is given and raises requests; the page, the dialogs and the saving stay with the window.
/// </summary>
public partial class TransactionsCard : UserControl
{
    public TransactionsCard()
    {
        InitializeComponent();
        TransactionsList.Styles.Add(new Style(selector => selector.OfType<ListBoxItem>())
        {
            Setters = { new Setter(BackgroundProperty, new Binding(nameof(TransactionItem.Background))) }
        });
        TransactionsList.ItemTemplate = new FuncDataTemplate<TransactionItem>(
            // Virtualized presenters can request a template with no item while scrolling.
            (item, _) => item is null ? null : TransactionRows.Build(item.Hit), true);
        TransactionsList.DoubleTapped += (_, _) =>
        {
            if (SelectedEntry is { } entry)
            {
                EditRequested?.Invoke(entry);
            }
        };
        TransactionsAddButton.Click += (_, _) => AddRequested?.Invoke();
        TransactionsRemoveButton.Click += (_, _) => RemoveRequested?.Invoke();
        TransactionsPreviousPage.Click += (_, _) => PreviousPageRequested?.Invoke();
        TransactionsNextPage.Click += (_, _) => NextPageRequested?.Invoke();
    }

    /// <summary>The add button was pressed.</summary>
    public event Action? AddRequested;

    /// <summary>The remove button was pressed; <see cref="SelectedEntry"/> says what, if anything, it refers to.</summary>
    public event Action? RemoveRequested;

    /// <summary>A row was double-tapped.</summary>
    public event Action<LedgerEntry>? EditRequested;

    public event Action? PreviousPageRequested;

    public event Action? NextPageRequested;

    public LedgerEntry? SelectedEntry => (TransactionsList.SelectedItem as TransactionItem)?.Hit.Entry;

    /// <summary>Sets the column headers, the empty message and the tooltips in the current language.</summary>
    public void Localize()
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

    /// <summary>
    /// Shows a page that starts at <paramref name="offset"/> in the full result. The rows are only rebuilt when they differ from the ones shown,
    /// and a selected row stays selected when it is still on the page.
    /// </summary>
    public void Render(TransactionsPage? page, int offset)
    {
        var total = page?.TotalCount ?? 0;
        var shown = page?.Hits.Count ?? 0;
        var first = total == 0 ? 0 : offset + 1;
        TransactionsHeading.Text = $"{Get("Transactions")} · {first:N0}-{offset + shown:N0} / {total:N0}";
        TransactionsEmpty.IsVisible = total == 0;

        DateOnly? previousDate = null;
        var alternateDay = false;
        var nextItems = page?.Hits.Select(hit =>
        {
            var date = hit.Entry.Draft.Date;
            if (previousDate is not null && previousDate != date)
            {
                alternateDay = !alternateDay;
            }

            previousDate = date;
            return new TransactionItem(hit, alternateDay);
        }).ToArray() ?? [];
        var currentItems = TransactionsList.ItemsSource?.OfType<TransactionItem>() ?? [];
        if (!currentItems.SequenceEqual(nextItems))
        {
            var selectedId = (TransactionsList.SelectedItem as TransactionItem)?.Hit.Entry.Id;
            TransactionsList.ItemsSource = nextItems;
            TransactionsList.SelectedItem = nextItems.FirstOrDefault(item => item.Hit.Entry.Id == selectedId);
        }

        TransactionsPreviousPage.IsEnabled = offset > 0;
        TransactionsNextPage.IsEnabled = offset + shown < total;
    }

    /// <summary>Forgets the rows shown, so the next <see cref="Render"/> rebuilds them (for example in another language).</summary>
    public void Invalidate() => TransactionsList.ItemsSource = null;

    public void ScrollToTop()
    {
        if (TransactionsList.Scroll is { } scroll)
        {
            scroll.Offset = new Vector(scroll.Offset.X, 0);
        }
    }

    private sealed record TransactionItem(TransactionsHit Hit, bool AlternateDay)
    {
        public IBrush Background => AlternateDay ? RowBrushes.Ivory : RowBrushes.Blue;

        public override string ToString()
        {
            var entry = Hit.Entry;
            return $"{entry.Draft.Date:yyyy-MM-dd} · {entry.Draft.Description} · {entry.Draft.Kind} · {AmountText(entry.Draft.Amount)} · {entry.AccountName}";
        }
    }
}
