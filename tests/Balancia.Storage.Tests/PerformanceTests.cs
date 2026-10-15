using System.Diagnostics;
using Balancia.Core;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Balancia.Storage.Tests;

public sealed class PerformanceTests(ITestOutputHelper output) : IDisposable
{
    private const int TransactionCount = 50_000;
    private const int WarmupRuns = 5;
    private const int SampleRuns = 30;
    private const int MedianIndex = SampleRuns / 2;
    private const int P95Index = (SampleRuns * 95 + 99) / 100 - 1;
    private readonly string path = Path.Combine(Path.GetTempPath(), "balancia-perf-" + Guid.NewGuid().ToString("N") + ".db");

    [Fact]
    public void ReadQueries_FiftyThousandSyntheticTransactions_ReportLatencyPercentiles()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("BALANCIA_PERF") == "1", "Set BALANCIA_PERF=1 to run.");

        // Arrange
        var store = new LedgerStore(path);
        store.Initialize();
        var account = store.SaveAccount(null, "Synthetic wallet", new DateOnly(2024, 1, 1), new Money(0));
        var category = store.SaveCategory(null, "Synthetic food", null);
        InsertExpenses(account, category);
        var deepCursor = ReadDeepCursor();
        var mix = new (string Name, Action Action)[]
        {
            ("first page", () => _ = store.ReadTransactions(new TransactionsFilter())),
            ("deep cursor page", () => _ = store.ReadTransactionsAfter(new TransactionsFilter(), deepCursor.Date, deepCursor.Id)),
            ("search", () => _ = store.ReadTransactions(new TransactionsFilter(Description: "expense 12345"))),
            ("combined filter", () => _ = store.ReadTransactions(new TransactionsFilter(AccountId: account, Kind: TransactionKind.Expense,
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

        // Act
        var results = mix.Select(query => (query.Name, Samples: Measure(query.Action))).ToArray();

        // Assert
        // Report only: the latencies are logged for comparison between runs, not checked against a budget.
        foreach (var (name, samples) in results)
        {
            output.WriteLine($"{name}: p95={samples[P95Index]:F1} ms, median={samples[MedianIndex]:F1} ms");
        }
    }

    private void InsertExpenses(string account, string category)
    {
        using var connection = new SqliteConnectionFactory(path).Open();
        using var transaction = connection.BeginTransaction();
        using var ledger = connection.CreateCommand();
        ledger.Transaction = transaction;
        ledger.CommandText = "INSERT INTO ledger VALUES($id,'Expense',$date,$description,$category,'')";
        ledger.Parameters.Add("$id", SqliteType.Text);
        ledger.Parameters.Add("$date", SqliteType.Text);
        ledger.Parameters.Add("$description", SqliteType.Text);
        ledger.Parameters.Add("$category", SqliteType.Text).Value = category;
        using var movement = connection.CreateCommand();
        movement.Transaction = transaction;
        movement.CommandText = "INSERT INTO movements VALUES($id,$account,-100)";
        movement.Parameters.Add("$id", SqliteType.Text);
        movement.Parameters.Add("$account", SqliteType.Text).Value = account;
        for (var i = 0; i < TransactionCount; i++)
        {
            var id = "synthetic-" + i.ToString("D6");
            ledger.Parameters["$id"].Value = id;
            ledger.Parameters["$date"].Value = new DateOnly(2024, 1, 1).AddDays(i % 960).ToString("yyyy-MM-dd");
            ledger.Parameters["$description"].Value = "Synthetic expense " + i;
            ledger.ExecuteNonQuery();
            movement.Parameters["$id"].Value = id;
            movement.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private (DateOnly Date, string Id) ReadDeepCursor()
    {
        using var connection = new SqliteConnectionFactory(path).Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT date,id FROM ledger WHERE kind<>'OpeningBalance' ORDER BY date DESC,id DESC LIMIT 1 OFFSET 24999";
        using var reader = command.ExecuteReader();
        reader.Read();
        return (DateOnly.Parse(reader.GetString(0)), reader.GetString(1));
    }

    private static double[] Measure(Action action)
    {
        for (var i = 0; i < WarmupRuns; i++)
        {
            action();
        }

        var samples = new double[SampleRuns];
        for (var i = 0; i < samples.Length; i++)
        {
            var started = Stopwatch.GetTimestamp();
            action();
            samples[i] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        }

        Array.Sort(samples);
        return samples;
    }

    public void Dispose() => File.Delete(path);
}
