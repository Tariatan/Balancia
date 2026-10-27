using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Balancia.Core;
using Balancia.Storage;
using Xunit;

using static Balancia.Desktop.Localization.UiText;

namespace Balancia.Desktop.Tests;

public sealed class TransactionsCardTests
{
    private static readonly DateOnly Day1 = new(2026, 10, 5);
    private static readonly DateOnly Day2 = new(2026, 10, 6);
    private static readonly DateOnly Day3 = new(2026, 10, 7);

    private static TransactionsHit Hit(string id, DateOnly date, string description = "Lunch", decimal francs = 12.5m,
        TransactionKind kind = TransactionKind.Expense, string account = "Everyday", string? destination = null,
        string? category = "Food / Lunch", decimal? accountEffect = null) =>
        new(new LedgerEntry(id, new TransactionDraft(kind, date, description, Money.FromFrancs(francs), "a", destination is null ? null : "b"),
                account, destination, category),
            accountEffect is { } effect ? Money.FromFrancs(effect) : null);

    private static TransactionsPage Page(IReadOnlyList<TransactionsHit> hits, long total, int offset = 0) =>
        new(hits, total, offset, 100, 1);

    private static (Window Window, TransactionsCard Card) Show()
    {
        var card = new TransactionsCard();
        var window = new Window { Width = 900, Height = 420, Content = card };
        window.Show();
        return (window, card);
    }

    private static T Find<T>(TransactionsCard card, string name) where T : Control => card.FindControl<T>(name)!;

    private static ListBox List(TransactionsCard card) => Find<ListBox>(card, "TransactionsList");

    private static ListBoxItem Row(TransactionsCard card, int index) => (ListBoxItem)List(card).ContainerFromIndex(index)!;

    private static string[] Cells(TransactionsCard card, int index) =>
        [.. ((Grid)Row(card, index).Presenter!.Child!).Children.OfType<TextBlock>().Select(cell => cell.Text ?? "")];

    private static TextBlock AmountCell(TransactionsCard card, int index) =>
        ((Grid)Row(card, index).Presenter!.Child!).Children.OfType<TextBlock>().ElementAt(1);

    private static void Render(TransactionsCard card, Window window, TransactionsPage? page, int offset = 0)
    {
        card.Render(page, offset);
        window.UpdateLayout();
    }

    [AvaloniaFact]
    public void Render_OnePageThatHoldsEverything_ShowsTheRangeAndDisablesBothPageButtons()
    {
        // Arrange
        var (window, card) = Show();

        // Act
        Render(card, window, Page([Hit("1", Day1), Hit("2", Day2)], 2));

        // Assert
        Assert.Equal($"{Get("Transactions")} · 1-2 / 2", Find<TextBlock>(card, "TransactionsHeading").Text);
        Assert.Equal(2, List(card).ItemCount);
        Assert.False(Find<TextBlock>(card, "TransactionsEmpty").IsVisible);
        Assert.False(Find<Button>(card, "TransactionsPreviousPage").IsEnabled);
        Assert.False(Find<Button>(card, "TransactionsNextPage").IsEnabled);
    }

    [AvaloniaFact]
    public void Render_FirstPageOfMany_EnablesOnlyNext()
    {
        // Arrange
        var (window, card) = Show();

        // Act
        Render(card, window, Page([Hit("1", Day1), Hit("2", Day2)], 250));

        // Assert
        Assert.True(Find<Button>(card, "TransactionsNextPage").IsEnabled);
        Assert.False(Find<Button>(card, "TransactionsPreviousPage").IsEnabled);
    }

    [AvaloniaFact]
    public void Render_MiddlePage_ShowsItsRangeAndEnablesBothButtons()
    {
        // Arrange
        var (window, card) = Show();

        // Act
        Render(card, window, Page([Hit("1", Day1), Hit("2", Day2)], 250, 100), offset: 100);

        // Assert
        Assert.Equal($"{Get("Transactions")} · 101-102 / 250", Find<TextBlock>(card, "TransactionsHeading").Text);
        Assert.True(Find<Button>(card, "TransactionsPreviousPage").IsEnabled);
        Assert.True(Find<Button>(card, "TransactionsNextPage").IsEnabled);
    }

    [AvaloniaFact]
    public void Render_LastPage_DisablesNext()
    {
        // Arrange
        var (window, card) = Show();

        // Act
        Render(card, window, Page([Hit("1", Day1), Hit("2", Day2)], 202, 200), offset: 200);

        // Assert
        Assert.True(Find<Button>(card, "TransactionsPreviousPage").IsEnabled);
        Assert.False(Find<Button>(card, "TransactionsNextPage").IsEnabled);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Render_NoTransactions_ShowsTheEmptyMessageAndAZeroRange(bool withPage)
    {
        // Arrange
        var (window, card) = Show();

        // Act
        Render(card, window, withPage ? Page([], 0) : null);

        // Assert
        Assert.True(Find<TextBlock>(card, "TransactionsEmpty").IsVisible);
        Assert.Equal($"{Get("Transactions")} · 0-0 / 0", Find<TextBlock>(card, "TransactionsHeading").Text);
        Assert.Equal(0, List(card).ItemCount);
    }

    [AvaloniaFact]
    public void Rows_AreShadedByDayAndAlternateWhenTheDateChanges()
    {
        // Arrange
        var (window, card) = Show();

        // Act
        Render(card, window, Page([Hit("1", Day1), Hit("2", Day1), Hit("3", Day2), Hit("4", Day2), Hit("5", Day3)], 5));

        // Assert
        Assert.Same(RowBrushes.Blue, Row(card, 0).Background);
        Assert.Same(RowBrushes.Blue, Row(card, 1).Background);
        Assert.Same(RowBrushes.Ivory, Row(card, 2).Background);
        Assert.Same(RowBrushes.Ivory, Row(card, 3).Background);
        Assert.Same(RowBrushes.Blue, Row(card, 4).Background);
    }

    [AvaloniaFact]
    public void Row_Expense_ShowsDateSignedAmountCategoryAccountAndDescription()
    {
        // Arrange
        var (window, card) = Show();

        // Act
        Render(card, window, Page([Hit("1", Day1)], 1));

        // Assert
        Assert.Equal([Day1.ToString("dd MMM yyyy", Culture), "− " + InputParsing.AmountText(Money.FromFrancs(12.5m)), "Food / Lunch", "Everyday", "Lunch"], Cells(card, 0));
        Assert.Same(TransactionRows.ExpenseBrush, AmountCell(card, 0).Foreground);
    }

    [AvaloniaFact]
    public void Row_IncomeAndTransfer_UseTheirSignAndColor()
    {
        // Arrange
        var (window, card) = Show();

        // Act
        Render(card, window, Page(
        [
            Hit("1", Day1, kind: TransactionKind.Income, description: "Salary", category: null),
            Hit("2", Day1, kind: TransactionKind.Transfer, description: "Move", account: "Everyday", destination: "Savings", category: null),
        ], 2));

        // Assert
        Assert.StartsWith("+ ", Cells(card, 0)[1]);
        Assert.Same(TransactionRows.IncomeBrush, AmountCell(card, 0).Foreground);
        Assert.Equal("—", Cells(card, 0)[2]);
        Assert.StartsWith("↔ ", Cells(card, 1)[1]);
        Assert.Same(TransactionRows.TransferBrush, AmountCell(card, 1).Foreground);
        Assert.Equal("Everyday → Savings", Cells(card, 1)[3]);
    }

    [AvaloniaTheory]
    [InlineData(-12.5, "− ")]
    [InlineData(12.5, "+ ")]
    public void Row_WithAnAccountEffect_ShowsThatEffectInsteadOfTheKind(double effect, string sign)
    {
        // Arrange
        var (window, card) = Show();

        // Act
        Render(card, window, Page([Hit("1", Day1, kind: TransactionKind.Transfer, accountEffect: (decimal)effect)], 1));

        // Assert
        Assert.Equal(sign + InputParsing.AmountText(Money.FromFrancs(12.5m)), Cells(card, 0)[1]);
    }

    [AvaloniaFact]
    public void Row_AccessibilityName_DescribesTheTransactionInPlainText()
    {
        // Arrange
        var (window, card) = Show();
        Render(card, window, Page([Hit("1", Day1, description: "Lunch")], 1));

        // Act
        var name = ControlAutomationPeer.CreatePeerForElement(Row(card, 0)).GetName();

        // Assert
        Assert.StartsWith("2026-10-05 · Lunch · Expense", name);
        Assert.EndsWith("Everyday", name);
    }

    [AvaloniaFact]
    public void Render_SamePageAgain_KeepsTheRowsAndTheSelection()
    {
        // Arrange
        var (window, card) = Show();
        Render(card, window, Page([Hit("1", Day1), Hit("2", Day2)], 2));
        List(card).SelectedIndex = 1;
        var items = List(card).ItemsSource;

        // Act
        Render(card, window, Page([Hit("1", Day1), Hit("2", Day2)], 2));

        // Assert
        Assert.Same(items, List(card).ItemsSource);
        Assert.Equal("2", card.SelectedEntry!.Id);
    }

    [AvaloniaFact]
    public void Render_ChangedPage_KeepsTheSelectionWhenItsEntryIsStillThere()
    {
        // Arrange
        var (window, card) = Show();
        Render(card, window, Page([Hit("1", Day1), Hit("2", Day2)], 2));
        List(card).SelectedIndex = 1;

        // Act
        Render(card, window, Page([Hit("2", Day2), Hit("3", Day3)], 2));

        // Assert
        Assert.Equal("2", card.SelectedEntry!.Id);
    }

    [AvaloniaFact]
    public void Render_ChangedPage_ClearsTheSelectionWhenItsEntryIsGone()
    {
        // Arrange
        var (window, card) = Show();
        Render(card, window, Page([Hit("1", Day1), Hit("2", Day2)], 2));
        List(card).SelectedIndex = 1;

        // Act
        Render(card, window, Page([Hit("3", Day3)], 1));

        // Assert
        Assert.Null(card.SelectedEntry);
    }

    [AvaloniaFact]
    public void Invalidate_ThenRender_RebuildsTheRowsAndLosesTheSelection()
    {
        // Arrange
        var (window, card) = Show();
        var page = Page([Hit("1", Day1), Hit("2", Day2)], 2);
        Render(card, window, page);
        List(card).SelectedIndex = 1;
        var items = List(card).ItemsSource;

        // Act
        card.Invalidate();
        Render(card, window, page);

        // Assert
        Assert.NotSame(items, List(card).ItemsSource);
        Assert.Equal(2, List(card).ItemCount);
        Assert.Null(card.SelectedEntry);
    }

    [AvaloniaFact]
    public void Localize_SetsTheColumnHeadersAndTheEmptyMessage()
    {
        // Arrange
        var (_, card) = Show();

        // Act
        card.Localize();

        // Assert
        Assert.Equal(Get("Date"), Find<TextBlock>(card, "TransactionsColumnDate").Text);
        Assert.Equal(Get("Amount"), Find<TextBlock>(card, "TransactionsColumnAmount").Text);
        Assert.Equal(Get("Category"), Find<TextBlock>(card, "TransactionsColumnCategory").Text);
        Assert.Equal(Get("Account"), Find<TextBlock>(card, "TransactionsColumnAccount").Text);
        Assert.Equal(Get("Description"), Find<TextBlock>(card, "TransactionsColumnDescription").Text);
        Assert.Equal(Get("No matching transactions."), Find<TextBlock>(card, "TransactionsEmpty").Text);
    }

    [AvaloniaFact]
    public void Buttons_Pressed_RaiseTheirRequests()
    {
        // Arrange
        var (_, card) = Show();
        var raised = new List<string>();
        card.AddRequested += () => raised.Add("add");
        card.RemoveRequested += () => raised.Add("remove");
        card.PreviousPageRequested += () => raised.Add("previous");
        card.NextPageRequested += () => raised.Add("next");

        // Act
        Find<Button>(card, "TransactionsAddButton").Press();
        Find<Button>(card, "TransactionsRemoveButton").Press();
        Find<Button>(card, "TransactionsPreviousPage").Press();
        Find<Button>(card, "TransactionsNextPage").Press();

        // Assert
        Assert.Equal(["add", "remove", "previous", "next"], raised);
    }

    [AvaloniaFact]
    public void SelectedEntry_NothingChosen_IsNullAndTheChosenEntryOtherwise()
    {
        // Arrange
        var (window, card) = Show();
        Render(card, window, Page([Hit("1", Day1), Hit("2", Day2)], 2));
        var before = card.SelectedEntry;

        // Act
        List(card).SelectedIndex = 0;

        // Assert
        Assert.Null(before);
        Assert.Equal("1", card.SelectedEntry!.Id);
    }

    [AvaloniaFact]
    public void Row_DoubleClicked_RaisesEditRequestedForThatEntry()
    {
        // Arrange
        var (window, card) = Show();
        Render(card, window, Page([Hit("1", Day1)], 1));
        var row = Row(card, 0);
        var point = row.TranslatePoint(new Point(row.Bounds.Width * 0.9, row.Bounds.Height / 2), window)!.Value;
        LedgerEntry? edited = null;
        card.EditRequested += entry => edited = entry;

        // Act
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);

        // Assert
        Assert.Equal("1", edited!.Id);
    }

    [AvaloniaFact]
    public void ScrollToTop_AfterScrollingDown_ReturnsToTheFirstRow()
    {
        // Arrange
        var (window, card) = Show();
        var hits = Enumerable.Range(0, 100).Select(i => Hit(i.ToString(), Day1.AddDays(i))).ToArray();
        Render(card, window, Page(hits, 100));
        var scroll = List(card).Scroll!;
        scroll.Offset = new Vector(0, 400);
        window.UpdateLayout();
        var scrolled = scroll.Offset.Y;

        // Act
        card.ScrollToTop();

        // Assert
        Assert.True(scrolled > 0);
        Assert.Equal(0, scroll.Offset.Y);
    }
}
