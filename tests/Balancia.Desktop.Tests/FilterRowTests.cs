using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Balancia.Core;
using Balancia.Storage;
using Xunit;

using static Balancia.Desktop.Localization.UiText;

namespace Balancia.Desktop.Tests;

public sealed class FilterRowTests
{
    private static (Window Window, FilterRow Row) Show()
    {
        var row = new FilterRow();
        var window = new Window { Width = 1100, Height = 200, Content = row };
        window.Show();
        return (window, row);
    }

    private static T Find<T>(FilterRow row, string name) where T : Control => row.FindControl<T>(name)!;

    private static void LoseFocus(Control control) => control.RaiseEvent(new FocusChangedEventArgs(InputElement.LostFocusEvent));

    private static string ColorOf(Control control) => ((ISolidColorBrush)control.GetValue(TemplatedControl.ForegroundProperty)!).Color.ToString();

    private static readonly string[] Inputs = ["FilterFrom", "FilterTo", "FilterSearch", "FilterMinimum", "FilterMaximum"];

    [AvaloniaFact]
    public void Kind_Selection_CommitsAndLocalizingAndShowingInputsPreserveItWithoutCommitting()
    {
        // Arrange
        var (_, row) = Show();
        var kind = Find<ComboBox>(row, "FilterKind");
        var commits = 0;
        row.ValuesCommitted += () => commits++;
        Assert.Equal(new[] { Get("All"), Get("Expenses"), Get("Income"), Get("Transfer") },
            kind.Items.Cast<Choice<TransactionKind?>>().Select(choice => choice.Label));

        // Act
        foreach (var (index, expected) in new TransactionKind?[] { null, TransactionKind.Expense, TransactionKind.Income, TransactionKind.Transfer }.Index())
        {
            kind.SelectedIndex = index;
            Assert.Equal(expected, row.Read().Kind);
        }
        row.Localize();
        var preserved = row.Read().Kind;
        row.ShowInputs(new TransactionsFilter());

        // Assert
        Assert.Equal(3, commits);
        Assert.Equal(TransactionKind.Transfer, preserved);
        Assert.Null(row.Read().Kind);
        var controls = ((Panel)kind.Parent!).Children;
        Assert.True(controls.IndexOf(Find<CalendarDatePicker>(row, "FilterTo")) < controls.IndexOf(kind));
        Assert.True(controls.IndexOf(kind) < controls.IndexOf(Find<TextBox>(row, "FilterSearch")));
    }
    // Periods

    [AvaloniaTheory]
    [InlineData("FilterButtonAll", "All")]
    [InlineData("FilterButtonToday", "Day")]
    [InlineData("FilterButtonWeek", "ThisWeek")]
    [InlineData("FilterButtonMonth", "ThisMonth")]
    [InlineData("FilterButtonYear", "ThisYear")]
    [InlineData("FilterButtonLast30Days", "Last30Days")]
    public void PeriodButton_Pressed_RaisesItsPeriod(string button, string period)
    {
        // Arrange
        var (_, row) = Show();
        var raised = new List<OverviewPeriod>();
        row.PeriodSelected += raised.Add;

        // Act
        Find<Button>(row, button).Press();

        // Assert
        Assert.Equal([Enum.Parse<OverviewPeriod>(period)], raised);
    }

    private static string[] Highlighted(FilterRow row) =>
        [.. new[] { "FilterButtonAll", "FilterButtonToday", "FilterButtonWeek", "FilterButtonMonth", "FilterButtonYear", "FilterButtonLast30Days" }
            .Where(name => Find<Button>(row, name).FontWeight == FontWeight.SemiBold)];

    [AvaloniaFact]
    public void ShowPeriod_HighlightsOnlyThatPeriodsButton()
    {
        // Arrange
        var (_, row) = Show();

        // Act
        row.ShowPeriod(OverviewPeriod.ThisYear);
        var first = Highlighted(row);
        row.ShowPeriod(OverviewPeriod.Day);

        // Assert
        Assert.Equal(["FilterButtonYear"], first);
        Assert.Equal(["FilterButtonToday"], Highlighted(row));
        Assert.Equal(Brush.Parse("#D8ECF3").ToString(), Find<Button>(row, "FilterButtonToday").Background!.ToString());
        Assert.Equal(Brush.Parse("#1C627E").ToString(), Find<Button>(row, "FilterButtonToday").Foreground!.ToString());
    }

    [AvaloniaFact]
    public void ShowPeriod_ACustomRange_HighlightsNoButton()
    {
        // Arrange
        var (_, row) = Show();
        row.ShowPeriod(OverviewPeriod.ThisMonth);

        // Act
        row.ShowPeriod(OverviewPeriod.Custom);

        // Assert
        Assert.Empty(Highlighted(row));
    }

    [AvaloniaFact]
    public void ShowPeriod_TheSamePeriodAgain_LeavesTheButtonsAloneUntilForgotten()
    {
        // Arrange
        var (_, row) = Show();
        row.ShowPeriod(OverviewPeriod.ThisMonth);
        Find<Button>(row, "FilterButtonMonth").FontWeight = FontWeight.Normal;

        // Act
        row.ShowPeriod(OverviewPeriod.ThisMonth);
        var untouched = Highlighted(row);
        row.ForgetShownPeriod();
        row.ShowPeriod(OverviewPeriod.ThisMonth);

        // Assert
        Assert.Empty(untouched);
        Assert.Equal(["FilterButtonMonth"], Highlighted(row));
    }

    // Localization

    [AvaloniaFact]
    public void Localize_NamesTheButtonsPlaceholdersAndTooltips()
    {
        // Arrange
        var (_, row) = Show();

        // Act
        row.Localize();

        // Assert
        Assert.Equal(Get("All"), Find<Button>(row, "FilterButtonAll").Content);
        Assert.Equal(Get("Today"), Find<Button>(row, "FilterButtonToday").Content);
        Assert.Equal(Get("Week"), Find<Button>(row, "FilterButtonWeek").Content);
        Assert.Equal(Get("Month"), Find<Button>(row, "FilterButtonMonth").Content);
        Assert.Equal(Get("Year"), Find<Button>(row, "FilterButtonYear").Content);
        Assert.Equal(Get("Last 30 days"), Find<Button>(row, "FilterButtonLast30Days").Content);
        Assert.Equal(Get("Select a date"), Find<CalendarDatePicker>(row, "FilterFrom").PlaceholderText);
        Assert.Equal(Get("Select a date"), Find<CalendarDatePicker>(row, "FilterTo").PlaceholderText);
        Assert.Equal(Get("Description"), Find<TextBox>(row, "FilterSearch").PlaceholderText);
        Assert.Equal(Get("Min amount"), Find<TextBox>(row, "FilterMinimum").PlaceholderText);
        Assert.Equal(Get("Max amount"), Find<TextBox>(row, "FilterMaximum").PlaceholderText);
        Assert.Equal(Get("Clear search"), ToolTip.GetTip(Find<Button>(row, "FilterClearSearch")));
        Assert.Equal(Get("Clear filters"), ToolTip.GetTip(Find<Button>(row, "FilterClearAll")));
    }

    // Showing values

    [AvaloniaFact]
    public void ShowInputs_PutsTheSearchTextAndAmountsIntoTheBoxes()
    {
        // Arrange
        var (_, row) = Show();

        // Act
        row.ShowInputs(new TransactionsFilter(Description: "coffee", Minimum: Money.FromFrancs(5m), Maximum: Money.FromFrancs(12.5m)));

        // Assert
        Assert.Equal("coffee", Find<TextBox>(row, "FilterSearch").Text);
        Assert.Equal("5.00", Find<TextBox>(row, "FilterMinimum").Text);
        Assert.Equal("12.50", Find<TextBox>(row, "FilterMaximum").Text);
    }

    [AvaloniaFact]
    public void ShowInputs_AnEmptyFilter_ClearsTheBoxes()
    {
        // Arrange
        var (_, row) = Show();
        row.ShowInputs(new TransactionsFilter(Description: "coffee", Minimum: Money.FromFrancs(5m), Maximum: Money.FromFrancs(9m)));

        // Act
        row.ShowInputs(new TransactionsFilter());

        // Assert
        Assert.Equal("", Find<TextBox>(row, "FilterSearch").Text);
        Assert.Equal("", Find<TextBox>(row, "FilterMinimum").Text);
        Assert.Equal("", Find<TextBox>(row, "FilterMaximum").Text);
    }

    [AvaloniaFact]
    public void ShowDates_PutsTheRangeIntoThePickersAndNullClearsThem()
    {
        // Arrange
        var (_, row) = Show();

        // Act
        row.ShowDates(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31));
        var shown = (Find<CalendarDatePicker>(row, "FilterFrom").SelectedDate, Find<CalendarDatePicker>(row, "FilterTo").SelectedDate);
        row.ShowDates(null, null);

        // Assert
        Assert.Equal((new DateTime(2026, 10, 1), new DateTime(2026, 10, 31)), shown);
        Assert.Null(Find<CalendarDatePicker>(row, "FilterFrom").SelectedDate);
        Assert.Null(Find<CalendarDatePicker>(row, "FilterTo").SelectedDate);
    }

    [AvaloniaFact]
    public void ShowInputsAndDates_WhileAnInputLosesFocus_DoNotRaiseValuesCommitted()
    {
        // Arrange
        var (_, row) = Show();
        var commits = 0;
        row.ValuesCommitted += () => commits++;
        var changes = 0;
        // Property changes are notified synchronously, so these run while the row is still showing the values.
        Find<TextBox>(row, "FilterSearch").PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty)
            {
                changes++;
                LoseFocus(Find<TextBox>(row, "FilterMinimum"));
            }
        };
        Find<CalendarDatePicker>(row, "FilterFrom").PropertyChanged += (_, e) =>
        {
            if (e.Property == CalendarDatePicker.SelectedDateProperty)
            {
                changes++;
                LoseFocus(Find<TextBox>(row, "FilterMinimum"));
            }
        };

        // Act
        row.ShowInputs(new TransactionsFilter(Description: "coffee"));
        row.ShowDates(new DateOnly(2026, 10, 1), null);
        var duringShowing = commits;
        LoseFocus(Find<TextBox>(row, "FilterMinimum"));

        // Assert
        Assert.Equal(2, changes);
        Assert.Equal(0, duringShowing);
        Assert.Equal(1, commits);
    }

    // Committing

    [AvaloniaTheory]
    [InlineData("FilterFrom")]
    [InlineData("FilterTo")]
    [InlineData("FilterSearch")]
    [InlineData("FilterMinimum")]
    [InlineData("FilterMaximum")]
    public void LeavingAnInput_RaisesValuesCommittedOnce(string name)
    {
        // Arrange
        var (_, row) = Show();
        var commits = 0;
        row.ValuesCommitted += () => commits++;

        // Act
        LoseFocus(Find<Control>(row, name));

        // Assert
        Assert.Equal(1, commits);
    }

    [AvaloniaFact]
    public void EnterInTheSearchBox_RaisesValuesCommittedAndIsHandled()
    {
        // Arrange
        var (window, row) = Show();
        var commits = 0;
        row.ValuesCommitted += () => commits++;
        var search = Find<TextBox>(row, "FilterSearch");
        var args = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter };

        // Act
        search.RaiseEvent(args);

        // Assert
        Assert.Equal(1, commits);
        Assert.True(args.Handled);
    }

    [AvaloniaFact]
    public void OtherKeysInTheSearchBox_DoNotCommit()
    {
        // Arrange
        var (_, row) = Show();
        var commits = 0;
        row.ValuesCommitted += () => commits++;
        var args = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.A };

        // Act
        Find<TextBox>(row, "FilterSearch").RaiseEvent(args);

        // Assert
        Assert.Equal(0, commits);
        Assert.False(args.Handled);
    }

    [AvaloniaFact]
    public void ClearSearchButton_EmptiesTheSearchBoxBeforeCommitting()
    {
        // Arrange
        var (_, row) = Show();
        row.ShowInputs(new TransactionsFilter(Description: "coffee"));
        var textAtCommit = new List<string?>();
        row.ValuesCommitted += () => textAtCommit.Add(Find<TextBox>(row, "FilterSearch").Text);

        // Act
        Find<Button>(row, "FilterClearSearch").Press();

        // Assert
        Assert.Equal([""], textAtCommit);
    }

    [AvaloniaFact]
    public void ClearAllButton_RaisesClearAllRequested()
    {
        // Arrange
        var (_, row) = Show();
        var raised = 0;
        row.ClearAllRequested += () => raised++;

        // Act
        Find<Button>(row, "FilterClearAll").Press();

        // Assert
        Assert.Equal(1, raised);
    }

    [AvaloniaTheory]
    [InlineData("FilterFrom")]
    [InlineData("FilterTo")]
    public void ClosingACalendar_CommitsAfterTheCurrentEventTurn(string name)
    {
        // Arrange
        var (_, row) = Show();
        var commits = 0;
        row.ValuesCommitted += () => commits++;
        var picker = Find<CalendarDatePicker>(row, name);

        // Act
        picker.IsDropDownOpen = true;
        picker.IsDropDownOpen = false;
        var beforeTheTurn = commits;
        Dispatcher.UIThread.RunJobs();

        // Assert
        Assert.Equal(0, beforeTheTurn);
        Assert.Equal(1, commits);
    }

    // Reading

    [AvaloniaFact]
    public void Read_EmptyInputs_GivesNoRestrictions()
    {
        // Arrange
        var (_, row) = Show();

        // Act
        var values = row.Read();

        // Assert
        Assert.Null(values.From);
        Assert.Null(values.To);
        Assert.Null(values.Minimum);
        Assert.Null(values.Maximum);
        Assert.True(string.IsNullOrEmpty(values.Description));
    }

    [AvaloniaFact]
    public void Read_FilledInputs_GivesTheirValues()
    {
        // Arrange
        var (_, row) = Show();
        row.ShowInputs(new TransactionsFilter(Description: " coffee ", Minimum: Money.FromFrancs(5m), Maximum: Money.FromFrancs(12.5m)));
        row.ShowDates(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31));

        // Act
        var values = row.Read();

        // Assert
        Assert.Equal(new FilterRowValues(" coffee ", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), Money.FromFrancs(5m), Money.FromFrancs(12.5m)), values);
    }

    [AvaloniaTheory]
    [InlineData("FilterMinimum")]
    [InlineData("FilterMaximum")]
    public void Read_AnAmountThatIsNotANumber_ThrowsFormatException(string name)
    {
        // Arrange
        var (_, row) = Show();
        Find<TextBox>(row, name).Text = "abc";

        // Act
        var read = row.Read;

        // Assert
        Assert.Throws<FormatException>(read);
    }

    // Tone

    [AvaloniaFact]
    public void Inputs_AreDarkWhenFilledAndMutedWhenEmpty()
    {
        // Arrange
        var (_, row) = Show();
        var empty = new[] { "FilterSearch", "FilterMinimum", "FilterMaximum", "FilterFrom", "FilterTo" }.Select(name => ColorOf(Find<Control>(row, name))).Distinct().Single();

        // Act
        row.ShowInputs(new TransactionsFilter(Description: "coffee", Minimum: Money.FromFrancs(5m), Maximum: Money.FromFrancs(9m)));
        row.ShowDates(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31));
        Dispatcher.UIThread.RunJobs();
        var filled = new[] { "FilterSearch", "FilterMinimum", "FilterMaximum", "FilterFrom", "FilterTo" }.Select(name => ColorOf(Find<Control>(row, name))).Distinct().Single();
        row.ShowInputs(new TransactionsFilter());
        row.ShowDates(null, null);
        Dispatcher.UIThread.RunJobs();

        // Assert
        Assert.Equal(Brush.Parse("#71838D").ToString(), empty);
        Assert.Equal(Brush.Parse("#263C48").ToString(), filled);
        Assert.All(Inputs, name => Assert.Equal(empty, ColorOf(Find<Control>(row, name))));
    }

    [AvaloniaFact]
    public void SearchBox_WithOnlySpaces_StaysMuted()
    {
        // Arrange
        var (_, row) = Show();

        // Act
        Find<TextBox>(row, "FilterSearch").Text = "   ";
        Dispatcher.UIThread.RunJobs();

        // Assert
        Assert.Equal(Brush.Parse("#71838D").ToString(), ColorOf(Find<TextBox>(row, "FilterSearch")));
    }
}