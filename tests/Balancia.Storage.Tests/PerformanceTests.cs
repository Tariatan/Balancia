using System.Diagnostics;
using Balancia.Core;
using Microsoft.Data.Sqlite;
using Xunit;
using Xunit.Abstractions;

namespace Balancia.Storage.Tests;

public sealed class PerformanceTests(ITestOutputHelper output)
{
    [Fact]
    public void P01_FiftyThousandSyntheticTransactions()
    {
        if (Environment.GetEnvironmentVariable("BALANCIA_PERF") != "1")
        {
            return;
        }

        var path = Path.Combine(Path.GetTempPath(), "balancia-perf-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var store = new LedgerStore(path);
            store.Initialize();
            var account = store.SaveAccount(null, "Synthetic wallet", new DateOnly(2024, 1, 1), new Money(0));
            var category = store.SaveCategory(null, "Synthetic food", null);
            using (var c = new SqliteConnectionFactory(path).Open())
            using (var tx = c.BeginTransaction())
            {
                using var ledger = c.CreateCommand();
                ledger.Transaction = tx;
                ledger.CommandText = "INSERT INTO ledger VALUES($id,'Expense',$date,$description,$category,'')";
                ledger.Parameters.Add("$id", SqliteType.Text);
                ledger.Parameters.Add("$date", SqliteType.Text);
                ledger.Parameters.Add("$description", SqliteType.Text);
                ledger.Parameters.Add("$category", SqliteType.Text).Value = category;
                using var movement = c.CreateCommand();
                movement.Transaction = tx;
                movement.CommandText = "INSERT INTO movements VALUES($id,$account,-100)";
                movement.Parameters.Add("$id", SqliteType.Text);
                movement.Parameters.Add("$account", SqliteType.Text).Value = account;
                for (var i = 0; i < 50_000; i++)
                {
                    var id = "synthetic-" + i.ToString("D6");
                    ledger.Parameters["$id"].Value = id;
                    ledger.Parameters["$date"].Value = new DateOnly(2024, 1, 1).AddDays(i % 960).ToString("yyyy-MM-dd");
                    ledger.Parameters["$description"].Value = "Synthetic expense " + i;
                    ledger.ExecuteNonQuery();
                    movement.Parameters["$id"].Value = id;
                    movement.ExecuteNonQuery();
                }
                tx.Commit();
            }
            (DateOnly Date, string Id) deepCursor;
            using (var c = new SqliteConnectionFactory(path).Open())
            using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = "SELECT date,id FROM ledger WHERE kind<>'OpeningBalance' ORDER BY date DESC,id DESC LIMIT 1 OFFSET 24999";
                using var reader = cmd.ExecuteReader();
                reader.Read();
                deepCursor = (DateOnly.Parse(reader.GetString(0)), reader.GetString(1));
            }
            var mix = new (string Name, Action Action)[]
            {
                ("first page", () => _ = store.ReadHistory(new HistoryFilter())),
                ("deep cursor page", () => _ = store.ReadHistoryAfter(new HistoryFilter(), deepCursor.Date, deepCursor.Id)),
                ("search", () => _ = store.ReadHistory(new HistoryFilter(Description: "expense 12345"))),
                ("combined filter", () => _ = store.ReadHistory(new HistoryFilter(AccountId: account, Kind: TransactionKind.Expense,
                    CategoryId: category, From: new DateOnly(2025, 1, 1), To: new DateOnly(2025, 12, 31),
                    Minimum: new Money(100), Maximum: new Money(100)))),
                ("dashboard", () => _ = store.ReadDesktopSnapshot()),
                ("committed edit and dashboard", () =>
                {
                    store.SaveTransaction("synthetic-000001", new TransactionDraft(TransactionKind.Expense, new DateOnly(2024, 1, 2),
                        "Synthetic edited", new Money(100), account, CategoryId: category));
                    _ = store.ReadDesktopSnapshot();
                })
            };
            foreach (var (name, action) in mix)
            {
                for (var i = 0; i < 5; i++)
                {
                    action();
                }

                var samples = new double[30];
                for (var i = 0; i < samples.Length; i++)
                {
                    var sw = Stopwatch.StartNew();
                    action();
                    sw.Stop();
                    samples[i] = sw.Elapsed.TotalMilliseconds;
                }
                Array.Sort(samples);
                output.WriteLine($"{name}: p95={samples[28]:F1} ms, median={samples[15]:F1} ms");
            }
        }
        finally { SqliteConnection.ClearAllPools(); File.Delete(path); }
    }
}
