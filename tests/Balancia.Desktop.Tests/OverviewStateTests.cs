using Balancia.Core;
using Balancia.Storage;
using Xunit;

namespace Balancia.Desktop.Tests;

public sealed class OverviewStateTests
{
    // 2026-10-13 is a Tuesday; its week is Monday 2026-10-12 to Sunday 2026-10-18.
    private static readonly DateOnly Today = new(2026, 10, 13);
    private static readonly DateOnly MonthStart = new(2026, 10, 1);
    private static readonly DateOnly MonthEnd = new(2026, 10, 31);

    private static readonly Category Food = new("food", "Food", null, "Food", false);
    private static readonly Category Lunch = new("lunch", "Lunch", "food", "Food / Lunch", false);
    private static readonly Category Dinner = new("dinner", "Dinner", "food", "Food / Dinner", false);
    private static readonly Category Travel = new("travel", "Travel", null, "Travel", false);
    private static readonly Category[] Categories = [Food, Lunch, Dinner, Travel];

    /// <summary>What the filter row passes when the user changes only the text: the dates the controls show are the period's.</summary>
    private static FilterApplication ApplyText(OverviewState state, string? description) =>
        state.ApplyFilterValues(description, MonthStart, MonthEnd, null, null, Today);

    [Fact]
    public void NewState_StartsOnThisMonthWithAnEmptyFilterOnTheFirstPage()
    {
        // Act
        var state = new OverviewState();

        // Assert
        Assert.Equal(OverviewPeriod.ThisMonth, state.Period);
        Assert.Equal(new TransactionsFilter(), state.Filter);
        Assert.Equal(0, state.Offset);
        Assert.Equal(FlowInterval.Week, state.TrendInterval);
    }

    [Fact]
    public void SelectPeriod_ThisWeek_FiltersToTheWeekAndZoomsTheTrendToDaysAndRestartsThePage()
    {
        // Arrange
        var state = new OverviewState();
        state.ChangePage(200);

        // Act
        state.SelectPeriod(OverviewPeriod.ThisWeek, Today);

        // Assert
        Assert.Equal(OverviewPeriod.ThisWeek, state.Period);
        Assert.Equal(new DateOnly(2026, 10, 12), state.Filter.From);
        Assert.Equal(new DateOnly(2026, 10, 18), state.Filter.To);
        Assert.Equal(FlowInterval.Day, state.TrendInterval);
        Assert.Equal(0, state.Offset);
    }

    [Fact]
    public void SelectPeriod_Last30Days_LeavesTheFilterDatesOpenAndTheQuerySuppliesThem()
    {
        // Arrange
        var state = new OverviewState();

        // Act
        state.SelectPeriod(OverviewPeriod.Last30Days, Today);
        var query = state.Query(Today);

        // Assert
        Assert.Null(state.Filter.From);
        Assert.Null(state.Filter.To);
        Assert.Equal(new DateOnly(2026, 9, 14), query.Filter.From);
        Assert.Equal(Today, query.Filter.To);
        Assert.Equal(FlowInterval.Week, state.TrendInterval);
    }

    [Fact]
    public void SelectPeriod_All_HasNoDatesAndZoomsTheTrendToMonths()
    {
        // Arrange
        var state = new OverviewState();

        // Act
        state.SelectPeriod(OverviewPeriod.All, Today);

        // Assert
        Assert.Null(state.Filter.From);
        Assert.Null(state.Filter.To);
        Assert.Null(state.Query(Today).Filter.From);
        Assert.Equal(FlowInterval.Month, state.TrendInterval);
    }

    [Fact]
    public void SelectPeriod_KeepsTheRestOfTheFilter()
    {
        // Arrange
        var state = new OverviewState();
        ApplyText(state, "lunch");
        state.ToggleAccount("a", true);

        // Act
        state.SelectPeriod(OverviewPeriod.Day, Today);

        // Assert
        Assert.Equal("lunch", state.Filter.Description);
        Assert.Equal("a", state.Filter.AccountId);
        Assert.Equal(Today, state.Filter.From);
    }

    [Fact]
    public void ChangePage_MovesByTheDeltaAndNeverBeforeTheFirstPage()
    {
        // Arrange
        var state = new OverviewState();

        // Act
        state.ChangePage(100);
        state.ChangePage(100);
        var afterTwoPages = state.Offset;
        state.ChangePage(-500);

        // Assert
        Assert.Equal(200, afterTwoPages);
        Assert.Equal(0, state.Offset);
    }

    [Fact]
    public void ApplyFilterValues_SameValuesAgain_IsUnchangedAndKeepsThePage()
    {
        // Arrange
        var state = new OverviewState();
        ApplyText(state, "lunch");
        state.ChangePage(100);

        // Act
        var result = ApplyText(state, "lunch");

        // Assert
        Assert.Equal(FilterApplication.Unchanged, result);
        Assert.Equal(100, state.Offset);
    }

    [Fact]
    public void ApplyFilterValues_TextChangeWithThePeriodDates_StaysOnThePeriodAndRestartsThePage()
    {
        // Arrange
        var state = new OverviewState();
        state.ChangePage(100);

        // Act
        var result = ApplyText(state, "lunch");

        // Assert
        Assert.Equal(FilterApplication.Applied, result);
        Assert.Equal(OverviewPeriod.ThisMonth, state.Period);
        Assert.Equal("lunch", state.Filter.Description);
        Assert.Equal(0, state.Offset);
    }

    [Fact]
    public void ApplyFilterValues_BlankDescription_IsStoredAsNoDescription()
    {
        // Arrange
        var state = new OverviewState();
        ApplyText(state, "lunch");

        // Act
        ApplyText(state, "   ");

        // Assert
        Assert.Null(state.Filter.Description);
    }

    [Fact]
    public void ApplyFilterValues_AmountRange_IsStored()
    {
        // Arrange
        var state = new OverviewState();

        // Act
        state.ApplyFilterValues(null, MonthStart, MonthEnd, Money.FromFrancs(5m), Money.FromFrancs(50m), Today);

        // Assert
        Assert.Equal(Money.FromFrancs(5m), state.Filter.Minimum);
        Assert.Equal(Money.FromFrancs(50m), state.Filter.Maximum);
    }

    [Fact]
    public void ApplyFilterValues_DatesDifferentFromThePeriod_BecomeACustomRange()
    {
        // Arrange
        var state = new OverviewState();

        // Act
        var result = state.ApplyFilterValues(null, new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 9), null, null, Today);

        // Assert
        Assert.Equal(FilterApplication.AppliedAsCustomRange, result);
        Assert.Equal(OverviewPeriod.Custom, state.Period);
        Assert.Equal(new DateOnly(2026, 10, 5), state.CustomFrom);
        Assert.Equal(new DateOnly(2026, 10, 9), state.CustomTo);
        Assert.Equal(new DateRange(new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 9)), state.Range(Today));
        Assert.Equal(FlowInterval.Day, state.TrendInterval);
    }

    [Fact]
    public void ApplyFilterValues_Last30DaysWithItsOwnDates_KeepsTheDatesOpen()
    {
        // Arrange
        var state = new OverviewState();
        state.SelectPeriod(OverviewPeriod.Last30Days, Today);

        // Act
        var result = state.ApplyFilterValues("lunch", new DateOnly(2026, 9, 14), Today, null, null, Today);

        // Assert
        Assert.Equal(FilterApplication.Applied, result);
        Assert.Equal(OverviewPeriod.Last30Days, state.Period);
        Assert.Equal("lunch", state.Filter.Description);
        Assert.Null(state.Filter.From);
        Assert.Null(state.Filter.To);
    }

    [Fact]
    public void Clear_CustomRange_FallsBackToThisMonthAndRemovesEveryFilter()
    {
        // Arrange
        var state = new OverviewState();
        state.ApplyFilterValues("lunch", new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 9), null, null, Today);
        state.ToggleAccount("a", true);
        state.ChangePage(100);

        // Act
        state.Clear();

        // Assert
        Assert.Equal(OverviewPeriod.ThisMonth, state.Period);
        Assert.Null(state.CustomFrom);
        Assert.Null(state.CustomTo);
        Assert.Equal(new TransactionsFilter(), state.Filter);
        Assert.Equal(0, state.Offset);
    }

    [Fact]
    public void Clear_PresetPeriod_StaysOnThePeriodAndKeepsTheTrendZoom()
    {
        // Arrange
        var state = new OverviewState();
        state.SelectPeriod(OverviewPeriod.Day, Today);
        state.ApplyFilterValues("lunch", Today, Today, null, null, Today);

        // Act
        state.Clear();

        // Assert
        Assert.Equal(OverviewPeriod.Day, state.Period);
        Assert.Equal(FlowInterval.Day, state.TrendInterval);
        Assert.Null(state.Filter.Description);
    }

    [Fact]
    public void ToggleAccount_SelectingAndDeselecting_MovesBetweenOneManyAndNone()
    {
        // Arrange
        var state = new OverviewState();
        state.ChangePage(100);

        // Act and Assert
        state.ToggleAccount("a", true);
        Assert.Equal("a", state.Filter.AccountId);
        Assert.Null(state.Filter.AccountIds);
        Assert.Equal(0, state.Offset);

        state.ToggleAccount("b", true);
        Assert.Null(state.Filter.AccountId);
        Assert.Equal(["a", "b"], state.Filter.AccountIds);

        state.ToggleAccount("a", false);
        Assert.Equal("b", state.Filter.AccountId);
        Assert.Null(state.Filter.AccountIds);

        state.ToggleAccount("b", false);
        Assert.Null(state.Filter.AccountId);
        Assert.Null(state.Filter.AccountIds);
        Assert.False(state.Filter.HasAccountFilter);
    }

    [Fact]
    public void IsAccountSelected_ReportsBothTheSingleAndTheManyForm()
    {
        // Arrange
        var state = new OverviewState();
        state.ToggleAccount("a", true);
        var singleSelected = state.IsAccountSelected("a");
        state.ToggleAccount("b", true);

        // Act and Assert
        Assert.True(singleSelected);
        Assert.True(state.IsAccountSelected("a"));
        Assert.True(state.IsAccountSelected("b"));
        Assert.False(state.IsAccountSelected("c"));
    }

    [Fact]
    public void ToggleCategory_Parent_SelectsItAndAllItsSubcategories()
    {
        // Arrange
        var state = new OverviewState();
        state.ChangePage(100);

        // Act
        state.ToggleCategory("food", true, Categories);

        // Assert
        Assert.Null(state.Filter.CategoryId);
        Assert.Equal(["dinner", "food", "lunch"], state.Filter.CategoryIds);
        Assert.Equal(0, state.Offset);
    }

    [Fact]
    public void ToggleCategory_DeselectingOneSubcategory_DeselectsTheParentToo()
    {
        // Arrange
        var state = new OverviewState();
        state.ToggleCategory("food", true, Categories);

        // Act
        state.ToggleCategory("lunch", false, Categories);

        // Assert
        Assert.Equal("dinner", state.Filter.CategoryId);
        Assert.Null(state.Filter.CategoryIds);
        Assert.False(state.IsCategorySelected("food"));
    }

    [Fact]
    public void ToggleCategory_SelectingTheLastSubcategory_SelectsTheParent()
    {
        // Arrange
        var state = new OverviewState();
        state.ToggleCategory("dinner", true, Categories);
        var parentSelectedWithOneChild = state.IsCategorySelected("food");

        // Act
        state.ToggleCategory("lunch", true, Categories);

        // Assert
        Assert.False(parentSelectedWithOneChild);
        Assert.Equal(["dinner", "food", "lunch"], state.Filter.CategoryIds);
    }

    [Fact]
    public void ToggleCategory_ParentDeselected_DeselectsItsSubcategories()
    {
        // Arrange
        var state = new OverviewState();
        state.ToggleCategory("food", true, Categories);
        state.ToggleCategory("travel", true, Categories);

        // Act
        state.ToggleCategory("food", false, Categories);

        // Assert
        Assert.Equal("travel", state.Filter.CategoryId);
        Assert.Null(state.Filter.CategoryIds);
    }

    [Fact]
    public void ToggleAllCategories_SelectsEveryCategoryThenNone()
    {
        // Arrange
        var state = new OverviewState();

        // Act
        state.ToggleAllCategories(Categories);
        var allSelected = state.Filter.CategoryIds;
        state.ToggleAllCategories(Categories);

        // Assert
        Assert.Equal(["dinner", "food", "lunch", "travel"], allSelected);
        Assert.False(state.Filter.HasCategoryFilter);
    }

    [Fact]
    public void ToggleAllCategories_SomeSelected_SelectsEveryCategory()
    {
        // Arrange
        var state = new OverviewState();
        state.ToggleCategory("travel", true, Categories);

        // Act
        state.ToggleAllCategories(Categories);

        // Assert
        Assert.Equal(["dinner", "food", "lunch", "travel"], state.Filter.CategoryIds);
    }

    [Fact]
    public void IsFiltered_PeriodAloneOrACustomRange_IsFalse()
    {
        // Arrange
        var fresh = new OverviewState();
        var week = new OverviewState();
        week.SelectPeriod(OverviewPeriod.ThisWeek, Today);
        var custom = new OverviewState();
        custom.ApplyFilterValues(null, new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 9), null, null, Today);

        // Act and Assert
        Assert.False(fresh.IsFiltered(Today));
        Assert.False(week.IsFiltered(Today));
        Assert.False(custom.IsFiltered(Today));
    }

    [Fact]
    public void IsFiltered_DescriptionAmountAccountOrCategory_IsTrue()
    {
        // Arrange
        var description = new OverviewState();
        ApplyText(description, "lunch");
        var amount = new OverviewState();
        amount.ApplyFilterValues(null, MonthStart, MonthEnd, Money.FromFrancs(5m), null, Today);
        var account = new OverviewState();
        account.ToggleAccount("a", true);
        var category = new OverviewState();
        category.ToggleCategory("travel", true, Categories);

        // Act and Assert
        Assert.True(description.IsFiltered(Today));
        Assert.True(amount.IsFiltered(Today));
        Assert.True(account.IsFiltered(Today));
        Assert.True(category.IsFiltered(Today));
    }

    [Fact]
    public void Query_OpenDates_AreTakenFromThePeriodAndTheOffsetAndPeriodAreCarried()
    {
        // Arrange
        var state = new OverviewState();
        state.ChangePage(100);

        // Act
        var query = state.Query(Today);

        // Assert
        Assert.Equal(MonthStart, query.Filter.From);
        Assert.Equal(MonthEnd, query.Filter.To);
        Assert.Equal(100, query.Offset);
        Assert.Equal(OverviewPeriod.ThisMonth, query.Period);
    }

    [Fact]
    public void Query_ExplicitDates_AreKept()
    {
        // Arrange
        var state = new OverviewState();
        state.ApplyFilterValues(null, new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 9), null, null, Today);

        // Act
        var query = state.Query(Today);

        // Assert
        Assert.Equal(new DateOnly(2026, 10, 5), query.Filter.From);
        Assert.Equal(new DateOnly(2026, 10, 9), query.Filter.To);
        Assert.Equal(OverviewPeriod.Custom, query.Period);
    }

    [Fact]
    public void StartWith_SetsThePeriodWithoutTouchingTheFilterOrTheTrend()
    {
        // Arrange
        var state = new OverviewState();

        // Act
        state.StartWith(OverviewPeriod.Last30Days);

        // Assert
        Assert.Equal(OverviewPeriod.Last30Days, state.Period);
        Assert.Equal(new TransactionsFilter(), state.Filter);
        Assert.Equal(FlowInterval.Week, state.TrendInterval);
    }

    [Fact]
    public void ZoomTrend_StepsThroughDayWeekMonthAndStopsAtBothEnds()
    {
        // Arrange
        var state = new OverviewState();

        // Act and Assert
        Assert.True(state.ZoomTrend(true));
        Assert.Equal(FlowInterval.Month, state.TrendInterval);
        Assert.False(state.ZoomTrend(true));
        Assert.Equal(FlowInterval.Month, state.TrendInterval);
        Assert.True(state.ZoomTrend(false));
        Assert.Equal(FlowInterval.Week, state.TrendInterval);
        Assert.True(state.ZoomTrend(false));
        Assert.Equal(FlowInterval.Day, state.TrendInterval);
        Assert.False(state.ZoomTrend(false));
        Assert.Equal(FlowInterval.Day, state.TrendInterval);
    }

    [Theory]
    [InlineData(OverviewPeriod.Day, null, null, FlowInterval.Day)]
    [InlineData(OverviewPeriod.ThisWeek, null, null, FlowInterval.Day)]
    [InlineData(OverviewPeriod.ThisMonth, null, null, FlowInterval.Week)]
    [InlineData(OverviewPeriod.Last30Days, null, null, FlowInterval.Week)]
    [InlineData(OverviewPeriod.ThisYear, null, null, FlowInterval.Month)]
    [InlineData(OverviewPeriod.All, null, null, FlowInterval.Month)]
    [InlineData(OverviewPeriod.Custom, "2026-10-01", "2026-10-31", FlowInterval.Day)] // under a month
    [InlineData(OverviewPeriod.Custom, "2026-10-01", "2026-11-01", FlowInterval.Week)] // a month, to under a year
    [InlineData(OverviewPeriod.Custom, "2026-01-01", "2026-12-31", FlowInterval.Week)]
    [InlineData(OverviewPeriod.Custom, "2026-01-01", "2027-01-01", FlowInterval.Month)] // a year or more
    [InlineData(OverviewPeriod.Custom, null, "2026-10-31", FlowInterval.Month)] // an open end has no span
    public void DefaultTrendInterval_PeriodAndSpan_PicksTheStartingZoom(OverviewPeriod period, string? from, string? to, FlowInterval expected)
    {
        // Arrange
        DateOnly? start = from is null ? null : DateOnly.Parse(from);
        DateOnly? end = to is null ? null : DateOnly.Parse(to);

        // Act
        var interval = OverviewState.DefaultTrendInterval(period, start, end);

        // Assert
        Assert.Equal(expected, interval);
    }
}
