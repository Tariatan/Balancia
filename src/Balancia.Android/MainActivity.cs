using System.IO.Compression;
using Android.App;
using Android.Content;
using Android.OS;
using Android.Widget;
using Balancia.Storage;

namespace Balancia.Android;

[Activity(Label = "@string/app_name", MainLauncher = true, Exported = true)]
public class MainActivity : Activity
{
    private const int PickSnapshot = 1001;
    private TextView? _status;
    private EditText? _search;
    private TextView? _results;
    private LedgerStore? _viewerStore;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SetContentView(Resource.Layout.activity_main);
        _status = FindViewById<TextView>(Resource.Id.Status);
        _search = FindViewById<EditText>(Resource.Id.Search);
        _results = FindViewById<TextView>(Resource.Id.Results);
        FindViewById<Button>(Resource.Id.SelectSnapshot)!.Click += (_, _) =>
        {
            // Custom .balancia extensions have no standard Android MIME type. Let
            // the provider show documents, then enforce the Balancia archive
            // format after selection.
            var picker = new Intent(Intent.ActionOpenDocument)
                .SetType("*/*")
                .AddCategory(Intent.CategoryOpenable);
            StartActivityForResult(picker, PickSnapshot);
        };
        FindViewById<Button>(Resource.Id.SearchButton)!.Click += (_, _) => SearchTransactions();
        _status!.Text = "No snapshot loaded. Windows is the only writer.";
    }

    protected override async void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode != PickSnapshot || resultCode != Result.Ok || data?.Data is null) return;
        try
        {
            _status!.Text = "Validating snapshot…";
            var archivePath = Path.Combine(FilesDir!.AbsolutePath, "incoming.balancia");
            await using (var input = ContentResolver!.OpenInputStream(data.Data))
            await using (var output = File.Create(archivePath)) await input!.CopyToAsync(output);
            var dbPath = Path.Combine(FilesDir.AbsolutePath, "viewer.db");
            var validator = new LedgerStore(dbPath);
            var manifest = validator.ValidateSnapshot(archivePath);
            var staged = dbPath + ".staged";
            using (var archive = ZipFile.OpenRead(archivePath))
            using (var input = archive.GetEntry("ledger.db")!.Open())
            using (var output = File.Create(staged)) await input.CopyToAsync(output);
            File.Move(staged, dbPath, true);
            var snapshot = new LedgerStore(dbPath).ReadSnapshot();
            _viewerStore = new LedgerStore(dbPath);
            _status.Text = $"Revision {manifest.Revision} · Net worth {snapshot.NetWorth.Francs:N2} CHF\n" +
                $"Income {snapshot.MonthlyIncome.Francs:N2} · Expenses {snapshot.MonthlyExpenses.Francs:N2} CHF\n" +
                $"{snapshot.Entries.Count} transactions · read-only viewer";
        }
        catch (Exception ex) { _status!.Text = "Snapshot rejected: " + ex.Message; }
    }

    private void SearchTransactions()
    {
        if (_viewerStore is null) { _results!.Text = "Load a snapshot first."; return; }
        try
        {
            var page = _viewerStore.ReadHistory(new HistoryFilter(_search?.Text), 0, 50);
            _results!.Text = page.Hits.Count == 0 ? "No matching transactions." :
                string.Join("\n", page.Hits.Select(h => $"{h.Entry.Draft.Date:yyyy-MM-dd} · {h.Entry.Draft.Kind} · {h.Entry.Draft.Amount.Francs:N2} CHF · {h.Entry.Draft.Description}"));
        }
        catch (Exception ex) { _results!.Text = "Search failed: " + ex.Message; }
    }
}
