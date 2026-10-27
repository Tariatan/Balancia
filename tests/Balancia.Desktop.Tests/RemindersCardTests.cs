using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Balancia.Core;
using Xunit;

namespace Balancia.Desktop.Tests;

public sealed class RemindersCardTests
{
    private static readonly DateOnly Today = new(2026, 10, 13);

    private static Reminder Reminder(string description, DateOnly occurrence, decimal francs = 10m, bool overdue = false) =>
        new(new ReminderTemplate(Guid.NewGuid().ToString("N"), description, occurrence, Money.FromFrancs(francs), 1, false),
            occurrence, overdue);

    private static (Window Window, RemindersCard Card) Show()
    {
        var card = new RemindersCard();
        var window = new Window { Width = 420, Height = 360, Content = card };
        window.Show();
        return (window, card);
    }

    private static T Find<T>(RemindersCard card, string name) where T : Control => card.FindControl<T>(name)!;

    [AvaloniaFact]
    public void Render_WithReminders_ListsThemAndTotalsTheAmounts()
    {
        // Arrange
        var (_, card) = Show();
        var reminders = new[]
        {
            Reminder("Rent", new DateOnly(2026, 10, 20), 100m),
            Reminder("Insurance", new DateOnly(2026, 10, 25), 50m),
            Reminder("Subscription", new DateOnly(2026, 11, 3), 900m),
        };

        // Act
        card.Render(reminders, Today);

        // Assert
        Assert.Equal(3, Find<ListBox>(card, "RemindersList").ItemCount);
        Assert.False(Find<TextBlock>(card, "RemindersEmpty").IsVisible);
        Assert.Equal(InputParsing.AmountText(Money.FromFrancs(1050m)), Find<TextBlock>(card, "ReminderTotalNetValue").Text);
        Assert.Equal(InputParsing.AmountText(Money.FromFrancs(150m)), Find<TextBlock>(card, "ReminderTotalUpcomingValue").Text);
    }

    [AvaloniaFact]
    public void Render_WithoutReminders_ShowsTheEmptyMessageAndZeroTotals()
    {
        // Arrange
        var (_, card) = Show();

        // Act
        card.Render([], Today);

        // Assert
        Assert.Equal(0, Find<ListBox>(card, "RemindersList").ItemCount);
        Assert.True(Find<TextBlock>(card, "RemindersEmpty").IsVisible);
        Assert.Equal(InputParsing.AmountText(Money.Zero), Find<TextBlock>(card, "ReminderTotalNetValue").Text);
    }

    [AvaloniaFact]
    public void Render_Again_ReplacesTheRows()
    {
        // Arrange
        var (_, card) = Show();
        card.Render([Reminder("Rent", new DateOnly(2026, 10, 20))], Today);

        // Act
        card.Render([Reminder("Insurance", new DateOnly(2026, 10, 25)), Reminder("Gym", new DateOnly(2026, 10, 26))], Today);

        // Assert
        Assert.Equal(2, Find<ListBox>(card, "RemindersList").ItemCount);
    }

    [AvaloniaFact]
    public void Render_RowsFromTwoMonthsOut_AreTintedBlueAndTheRestIvory()
    {
        // Arrange
        var (window, card) = Show();
        var list = Find<ListBox>(card, "RemindersList");

        // Act
        card.Render(
        [
            Reminder("This month", new DateOnly(2026, 10, 20)),
            Reminder("Next month", new DateOnly(2026, 11, 30)),
            Reminder("Two months out", new DateOnly(2026, 12, 1)),
        ], Today);
        window.UpdateLayout();

        // Assert
        IBrush? Background(int index) => (list.ContainerFromIndex(index) as ListBoxItem)?.Background;
        Assert.Same(RowBrushes.Ivory, Background(0));
        Assert.Same(RowBrushes.Ivory, Background(1));
        Assert.Same(RowBrushes.Blue, Background(2));
    }

    [AvaloniaFact]
    public void AddButton_Pressed_RaisesAddRequested()
    {
        // Arrange
        var (_, card) = Show();
        var raised = 0;
        card.AddRequested += () => raised++;

        // Act
        Find<Button>(card, "ReminderAddButton").Press();

        // Assert
        Assert.Equal(1, raised);
    }

    [AvaloniaFact]
    public void RemoveButton_PressedWithoutSelection_RaisesRemoveRequestedWithNothingSelected()
    {
        // Arrange
        var (_, card) = Show();
        card.Render([Reminder("Rent", new DateOnly(2026, 10, 20))], Today);
        Reminder? selectedWhenRaised = Reminder("sentinel", Today);
        card.RemoveRequested += () => selectedWhenRaised = card.Selected;

        // Act
        Find<Button>(card, "ReminderRemoveButton").Press();

        // Assert
        Assert.Null(selectedWhenRaised);
    }

    [AvaloniaFact]
    public void RemoveButton_PressedWithSelection_ExposesTheSelectedReminder()
    {
        // Arrange
        var (_, card) = Show();
        var rent = Reminder("Rent", new DateOnly(2026, 10, 20));
        card.Render([rent, Reminder("Insurance", new DateOnly(2026, 10, 25))], Today);
        Find<ListBox>(card, "RemindersList").SelectedIndex = 0;
        Reminder? selectedWhenRaised = null;
        card.RemoveRequested += () => selectedWhenRaised = card.Selected;

        // Act
        Find<Button>(card, "ReminderRemoveButton").Press();

        // Assert
        Assert.Same(rent, selectedWhenRaised);
    }

    [AvaloniaFact]
    public void Row_DoubleClicked_RaisesEditRequestedForThatReminder()
    {
        // Arrange
        var (window, card) = Show();
        var rent = Reminder("Rent", new DateOnly(2026, 10, 20));
        card.Render([rent], Today);
        window.UpdateLayout();
        var list = Find<ListBox>(card, "RemindersList");
        var row = (ListBoxItem)list.ContainerFromIndex(0)!;
        var point = row.TranslatePoint(new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), window)!.Value;
        Reminder? edited = null;
        card.EditRequested += reminder => edited = reminder;

        // Act
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);

        // Assert
        Assert.Same(rent, edited);
    }
}
