using Balancia.Core;

namespace Balancia.Storage;

/// <summary>The totals a read-only viewer shows for one ledger.</summary>
public sealed record LedgerSummary(Money NetWorth, Money MonthlyIncome, Money MonthlyExpenses,
    long TransactionCount, long Revision, IReadOnlyList<Account> Accounts);

/// <summary>A read-only view of a ledger database. It opens the file read-only and exposes no write operation, so a viewer cannot change financial data.</summary>
public sealed class LedgerReader
{
    private readonly LedgerStore store;

    private LedgerReader(LedgerStore store) => this.store = store;

    public static LedgerReader Open(string path, TimeProvider? clock = null) => new(new LedgerStore(path, clock, readOnly: true));

    public LedgerSummary ReadSummary()
    {
        var overview = store.ReadDesktopSnapshot();
        var count = store.ReadTransactions(new TransactionsFilter(), 0, 1).TotalCount;
        return new LedgerSummary(overview.NetWorth, overview.MonthlyIncome, overview.MonthlyExpenses, count, overview.Revision, overview.Accounts);
    }

    public TransactionsPage ReadTransactions(TransactionsFilter filter, int offset = 0, int pageSize = 100) =>
        store.ReadTransactions(filter, offset, pageSize);

    public (string DatasetId, long Revision) ReadIdentity() => store.ReadLiveMetadata();
}
