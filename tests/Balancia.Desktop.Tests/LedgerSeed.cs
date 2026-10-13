using Balancia.Core;
using Balancia.Storage;

namespace Balancia.Desktop.Tests;

/// <summary>Synthetic ledger data for UI tests, dated relative to today so period filters behave the same on any day.</summary>
internal static class LedgerSeed
{
    public static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    public static string Account(LedgerStore store, string name = "Everyday", DateOnly? opened = null) =>
        store.SaveAccount(null, name, opened ?? Today.AddDays(-500), Money.FromFrancs(1000m));

    public static string Expense(LedgerStore store, string account, string description, DateOnly date, decimal francs = 10m) =>
        store.SaveTransaction(null, new TransactionDraft(TransactionKind.Expense, date, description, Money.FromFrancs(francs), account));

    /// <summary>
    /// Three transactions that every period filter separates the same way on any day:
    /// today, 40 days ago (always an earlier month and outside the last 30 days) and 400 days ago (always an earlier year).
    /// </summary>
    public static void ThreeEras(LedgerStore store)
    {
        var account = Account(store);
        Expense(store, account, "Coffee beans", Today);
        Expense(store, account, "Rent", Today.AddDays(-40));
        Expense(store, account, "Coffee machine", Today.AddDays(-400));
    }
}
