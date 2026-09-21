using System.IO.Compression;
using _Microsoft.Android.Resource.Designer;
using Android.Content;
using Balancia.Storage;

namespace Balancia.Android;

[Activity(Label = "@string/app_name", MainLauncher = true, Exported = true)]
public class MainActivity : Activity
{
    private const int PickSnapshot = 1001;
    private TextView? status;
    private EditText? search;
    private TextView? results;
    private LedgerStore? viewerStore;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SetContentView(ResourceConstant.Layout.activity_main);
        status = FindViewById<TextView>(ResourceConstant.Id.Status);
        search = FindViewById<EditText>(ResourceConstant.Id.Search);
        results = FindViewById<TextView>(ResourceConstant.Id.Results);
        FindViewById<Button>(ResourceConstant.Id.SelectSnapshot)!.Click += (_, _) =>
        {
            // Custom .balancia extensions have no standard Android MIME type. Let
            // the provider show documents, then enforce the Balancia archive
            // format after selection.
            var picker = new Intent(Intent.ActionOpenDocument)
                .SetType("*/*")
                .AddCategory(Intent.CategoryOpenable);
            StartActivityForResult(picker, PickSnapshot);
        };
        FindViewById<Button>(ResourceConstant.Id.SearchButton)!.Click += (_, _) => SearchTransactions();
        status!.Text = "No snapshot loaded. Windows is the only writer.";
    }

    protected override async void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode != PickSnapshot || resultCode != Result.Ok || data?.Data is null)
        {
            return;
        }

        try
        {
            status!.Text = "Validating snapshot…";
            var archivePath = Path.Combine(FilesDir!.AbsolutePath, "incoming.balancia");
            await using (var input = ContentResolver!.OpenInputStream(data.Data))
            await using (var output = File.Create(archivePath))
            {
                await input!.CopyToAsync(output);
            }

            var dbPath = Path.Combine(FilesDir.AbsolutePath, "viewer.db");
            var validator = new LedgerStore(dbPath);
            var manifest = validator.ValidateSnapshot(archivePath);
            var staged = dbPath + ".staged";
            using (var archive = ZipFile.OpenRead(archivePath))
            using (var input = archive.GetEntry("ledger.db")!.Open())
            using (var output = File.Create(staged))
            {
                await input.CopyToAsync(output);
            }

            File.Move(staged, dbPath, true);
            var snapshot = new LedgerStore(dbPath).ReadSnapshot();
            viewerStore = new LedgerStore(dbPath);
            status.Text = $"Revision {manifest.Revision} · Net worth {snapshot.NetWorth.Francs:N2}\n" +
                $"Income {snapshot.MonthlyIncome.Francs:N2} · Expenses {snapshot.MonthlyExpenses.Francs:N2}\n" +
                $"{snapshot.Entries.Count} transactions · read-only viewer";
        }
        catch (Exception ex) { status!.Text = "Snapshot rejected: " + ex.Message; }
    }

    private void SearchTransactions()
    {
        if (viewerStore is null)
        {
            results!.Text = "Load a snapshot first.";
            return;
        }
        try
        {
            var page = viewerStore.ReadHistory(new HistoryFilter(search?.Text), 0, 50);
            results!.Text = page.Hits.Count == 0 ? "No matching transactions." :
                string.Join("\n", page.Hits.Select(h => $"{h.Entry.Draft.Date:yyyy-MM-dd} · {h.Entry.Draft.Kind} · {h.Entry.Draft.Amount.Francs:N2} · {h.Entry.Draft.Description}"));
        }
        catch (Exception ex) { results!.Text = "Search failed: " + ex.Message; }
    }
}
