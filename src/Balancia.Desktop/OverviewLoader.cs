using Balancia.Core;
using Balancia.Storage;

namespace Balancia.Desktop;

/// <summary>What the overview shows: the period, the effective filter (period dates already filled in) and the page of transactions.</summary>
internal sealed record OverviewQuery(TransactionsFilter Filter, int Offset, OverviewPeriod Period);

/// <summary>Everything one overview render needs, read together and never changed afterwards.</summary>
internal sealed record OverviewData(OverviewQuery Query, LedgerSnapshot Snapshot, IReadOnlyList<Reminder> Reminders,
    TransactionsPage Transactions, FlowAnalytics Analytics, FlowAverages Averages);

/// <summary>Reads everything the overview needs for a query in one step.</summary>
internal static class OverviewLoader
{
    internal static async Task<OverviewData> LoadAsync(LedgerStore store, OverviewQuery query)
    {
        var averageInterval = OverviewPeriods.IntervalFor(query.Period);
        var averageFilter = query.Filter with { From = OverviewPeriods.AverageFrom(query.Period, query.Filter.From) };

        // Each store call opens its own connection, so the reads can run concurrently.
        var snapshot = Task.Run(() => store.ReadDesktopSnapshotForFilter(query.Filter));
        var reminders = Task.Run(() => store.ReadReminders());
        var transactions = Task.Run(() => store.ReadTransactions(query.Filter, query.Offset));
        var analytics = Task.Run(() => store.ReadFlowAnalytics(query.Filter));
        var averages = Task.Run(() => store.ReadFlowAverages(averageFilter, averageInterval));
        await Task.WhenAll(snapshot, reminders, transactions, analytics, averages);
        return new OverviewData(query, snapshot.Result, reminders.Result, transactions.Result, analytics.Result, averages.Result);
    }
}
