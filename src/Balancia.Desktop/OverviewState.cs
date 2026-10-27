using Balancia.Core;
using Balancia.Storage;

namespace Balancia.Desktop;

/// <summary>What <see cref="OverviewState.ApplyFilterValues"/> did with the typed filter values.</summary>
internal enum FilterApplication
{
    /// <summary>The values equal the current filter; nothing changed.</summary>
    Unchanged,

    /// <summary>The filter changed and the period stays as it was.</summary>
    Applied,

    /// <summary>The filter changed and its dates no longer match the period, so the overview now shows a custom range.</summary>
    AppliedAsCustomRange,
}

/// <summary>
/// What the user has chosen on the overview: the period, the filter, the transactions page and the trend chart's zoom.
/// It holds no controls and reads no clock, so every rule for how a choice changes the others (a new period resets the page,
/// typing dates makes a custom range, selecting a category selects its subcategories) is plain code.
/// </summary>
internal sealed class OverviewState
{
    public TransactionsFilter Filter { get; private set; } = new();

    public OverviewPeriod Period { get; private set; } = OverviewPeriod.ThisMonth;

    public DateOnly? CustomFrom { get; private set; }

    public DateOnly? CustomTo { get; private set; }

    public int Offset { get; private set; }

    public FlowInterval TrendInterval { get; private set; } = FlowInterval.Week;

    /// <summary>The dates the period covers on <paramref name="today"/>.</summary>
    public DateRange Range(DateOnly today) => OverviewPeriods.Range(Period, today, CustomFrom, CustomTo);

    /// <summary>The query to load: the filter with any date it leaves open taken from the period.</summary>
    public OverviewQuery Query(DateOnly today)
    {
        var range = Range(today);
        return new OverviewQuery(Filter with { From = Filter.From ?? range.From, To = Filter.To ?? range.To }, Offset, Period);
    }

    /// <summary>Whether anything beyond the period narrows the overview, which changes how its scope is labelled.</summary>
    public bool IsFiltered(DateOnly today)
    {
        var range = Range(today);
        return !string.IsNullOrEmpty(Filter.Description) ||
            Filter.HasAccountFilter || Filter.Kind is not null ||
            Filter.HasCategoryFilter || Filter.Minimum is not null ||
            Filter.Maximum is not null ||
            (Filter.From ?? range.From) != range.From ||
            (Filter.To ?? range.To) != range.To;
    }

    /// <summary>The category the overview is filtered to when it is a single category that has subcategories; otherwise null.</summary>
    public Category? FilteredParent(IReadOnlyList<Category> categories)
    {
        var filtered = categories.FirstOrDefault(category => category.Id == Filter.CategoryId);
        return filtered is not null && categories.Any(category => category.ParentId == filtered.Id) ? filtered : null;
    }

    public bool IsAccountSelected(string id) => Filter.AccountId == id || Filter.AccountIds?.Contains(id) == true;

    public bool IsCategorySelected(string id) => Filter.CategoryId == id || Filter.CategoryIds?.Contains(id) == true;

    /// <summary>Sets the period without touching the filter, for the period the overview opens on.</summary>
    public void StartWith(OverviewPeriod period) => Period = period;

    /// <summary>Switches to a preset period: the page restarts, the filter takes the period's dates and the trend zoom follows the period.</summary>
    public void SelectPeriod(OverviewPeriod period, DateOnly today)
    {
        Period = period;
        Offset = 0;
        var range = Range(today);
        // Last 30 days moves with the clock, so its filter leaves the dates open and the period supplies them on every load.
        Filter = Filter with
        {
            From = period == OverviewPeriod.Last30Days ? null : range.From,
            To = period == OverviewPeriod.Last30Days ? null : range.To,
        };
        TrendInterval = DefaultTrendInterval(period, range.From, range.To);
    }

    /// <summary>Applies the values typed into the filter row. Dates that differ from the period's make it a custom range.</summary>
    public FilterApplication ApplyFilterValues(string? description, DateOnly? from, DateOnly? to, Money? minimum, Money? maximum, DateOnly today)
    {
        var periodRange = Range(today);
        var next = Filter with
        {
            Description = string.IsNullOrWhiteSpace(description) ? null : description,
            From = from,
            To = to,
            Minimum = minimum,
            Maximum = maximum,
        };
        if (next == Filter)
        {
            return FilterApplication.Unchanged;
        }

        var customRange = next.From != periodRange.From || next.To != periodRange.To;
        if (customRange)
        {
            CustomFrom = next.From;
            CustomTo = next.To;
            Period = OverviewPeriod.Custom;
            TrendInterval = DefaultTrendInterval(Period, next.From, next.To);
        }

        Filter = Period == OverviewPeriod.Last30Days ? next with { From = null, To = null } : next;
        Offset = 0;
        return customRange ? FilterApplication.AppliedAsCustomRange : FilterApplication.Applied;
    }

    /// <summary>Removes every filter and restarts the page. A custom range falls back to this month; a preset period stays.</summary>
    public void Clear()
    {
        Filter = new TransactionsFilter();
        Offset = 0;
        if (Period == OverviewPeriod.Custom)
        {
            Period = OverviewPeriod.ThisMonth;
            CustomFrom = null;
            CustomTo = null;
        }
    }

    public void ToggleAccount(string id, bool selected)
    {
        var ids = new HashSet<string>(Filter.AccountIds ?? []);
        if (Filter.AccountId is { } single)
        {
            ids.Add(single);
        }

        if (selected)
        {
            ids.Add(id);
        }
        else
        {
            ids.Remove(id);
        }

        Filter = Filter with
        {
            AccountId = ids.Count == 1 ? ids.Single() : null,
            AccountIds = ids.Count > 1 ? ids.Order().ToArray() : null,
        };
        Offset = 0;
    }

    /// <summary>Selects or deselects a category together with its subcategories, and keeps its parent selected exactly when all of the parent's children are.</summary>
    public void ToggleCategory(string id, bool selected, IReadOnlyList<Category> categories)
    {
        var ids = new HashSet<string>(Filter.CategoryIds ?? []);
        if (Filter.CategoryId is { } single)
        {
            ids.Add(single);
        }

        var affectedIds = categories
            .Where(category => category.Id == id || category.ParentId == id)
            .Select(category => category.Id);
        if (selected)
        {
            ids.UnionWith(affectedIds);
        }
        else
        {
            ids.ExceptWith(affectedIds);
        }

        var parentId = categories.Single(category => category.Id == id).ParentId;
        if (parentId is not null)
        {
            var allChildrenSelected = categories
                .Where(category => category.ParentId == parentId)
                .All(category => ids.Contains(category.Id));
            if (allChildrenSelected)
            {
                ids.Add(parentId);
            }
            else
            {
                ids.Remove(parentId);
            }
        }

        SelectCategories(ids);
    }

    /// <summary>Deselects every category when all are selected, otherwise selects them all.</summary>
    public void ToggleAllCategories(IReadOnlyList<Category> categories)
    {
        var ids = categories.All(category => IsCategorySelected(category.Id))
            ? new HashSet<string>()
            : categories.Select(category => category.Id).ToHashSet();
        SelectCategories(ids);
    }

    /// <summary>Moves the page by <paramref name="delta"/> transactions, never before the first.</summary>
    public void ChangePage(int delta) => Offset = Math.Max(0, Offset + delta);

    /// <summary>Zooms the trend chart one step out (coarser buckets) or in; returns false at the end of the range.</summary>
    public bool ZoomTrend(bool zoomOut)
    {
        var next = (TrendInterval, zoomOut) switch
        {
            (FlowInterval.Day, true) => FlowInterval.Week,
            (FlowInterval.Week, true) => FlowInterval.Month,
            (FlowInterval.Month, false) => FlowInterval.Week,
            (FlowInterval.Week, false) => FlowInterval.Day,
            _ => TrendInterval,
        };
        if (next == TrendInterval)
        {
            return false;
        }

        TrendInterval = next;
        return true;
    }

    /// <summary>The trend zoom a period starts with: finer for short spans, coarser for long ones.</summary>
    public static FlowInterval DefaultTrendInterval(OverviewPeriod period, DateOnly? from, DateOnly? to) => period switch
    {
        OverviewPeriod.Day or OverviewPeriod.ThisWeek => FlowInterval.Day,
        OverviewPeriod.ThisMonth or OverviewPeriod.Last30Days => FlowInterval.Week,
        OverviewPeriod.ThisYear or OverviewPeriod.All => FlowInterval.Month,
        _ when from is { } start && to is { } end && end < start.AddMonths(1) => FlowInterval.Day,
        _ when from is { } start && to is { } end && end < start.AddYears(1) => FlowInterval.Week,
        _ => FlowInterval.Month,
    };

    private void SelectCategories(HashSet<string> ids)
    {
        Filter = Filter with
        {
            CategoryId = ids.Count == 1 ? ids.Single() : null,
            CategoryIds = ids.Count > 1 ? ids.Order().ToArray() : null,
        };
        Offset = 0;
    }
}
