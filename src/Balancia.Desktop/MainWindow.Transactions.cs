using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Balancia.Core;
using Balancia.Storage;
using Microsoft.Data.Sqlite;

namespace Balancia.Desktop;

public partial class MainWindow
{
    private static ListBox HistoryList(IReadOnlyList<HistoryHit> hits) => new()
    {
        ItemsSource = hits.Select(hit => new HistoryItem(hit)).ToArray(),
        ItemTemplate = new FuncDataTemplate<HistoryItem>((item, _) => HistoryRow(item.Hit), true),
        HorizontalAlignment = HorizontalAlignment.Stretch,
        Background = Brushes.Transparent,
        BorderThickness = new Thickness(0)
    };

    private static Grid HistoryRow(HistoryHit hit)
    {
        var entry = hit.Entry;
        var effect = hit.AccountEffect;
        var amount = effect ?? entry.Draft.Amount;
        var sign = effect is { } accountEffect ? accountEffect.Centimes < 0 ? "− " : "+ " :
            entry.Draft.Kind switch
            {
                TransactionKind.Expense => "− ",
                TransactionKind.Income => "+ ",
                _ => "↔ "
            };

        return HistoryRow(entry.Draft.Date.ToString("dd MMM yyyy", CultureInfo.CurrentCulture),
            string.IsNullOrWhiteSpace(entry.Draft.Description) ? "" : entry.Draft.Description,
            entry.CategoryPath ?? "—",
            entry.DestinationName is null ? entry.AccountName : $"{entry.AccountName} → {entry.DestinationName}",
            sign + AmountText(new Money(Math.Abs(amount.Centimes))), false, entry.Draft.Kind);
    }

    private static Grid HistoryRow(string date, string description, string category, string account, string amount,
        bool header, TransactionKind? kind = null)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("120,120,200,130,*"),
            MinHeight = header ? 31 : 20
        };
        var values = new[] { date, amount, category, account, description };

        for (var i = 0; i < values.Length; i++)
        {
            var amountColor = kind switch
            {
                TransactionKind.Income => "#2C8B6D",
                TransactionKind.Expense => "#B95D4D",
                TransactionKind.Transfer => "#1B4F72",
                _ => "#263C48"
            };
            var cell = new TextBlock
            {
                Text = values[i],
                FontSize = header ? 12 : 13,
                FontWeight = !header && i == 1 ? FontWeight.Bold : FontWeight.Normal,
                Foreground = Brush.Parse(header ? "#71838D" : i == 1 ? amountColor : "#263C48"),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextAlignment = i == 1 ? TextAlignment.Right : TextAlignment.Left,
                Margin = new Thickness(0, 0, 30, 0)
            };
            AddColumn(row, cell, i);
        }
        return row;
    }

    private async Task EditTransaction(LedgerEntry? entry)
    {
        var existing = entry?.Draft;
        var accounts = _snapshot!.Accounts.Where(a => !a.Archived || a.Id == existing?.AccountId || a.Id == existing?.DestinationId).ToArray();
        if (accounts.Length == 0)
        {
            Status.Text = "Add an active account before entering transactions.";
            return;
        }
        var kind = new ComboBox
        {
            ItemsSource = Enum.GetValues<TransactionKind>(),
            SelectedItem = existing?.Kind ?? TransactionKind.Expense,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var date = DateInput(existing?.Date ?? DateOnly.FromDateTime(DateTime.Today));
        var description = Input(existing?.Description ?? "");
        var amount = Input(existing?.Amount.Francs.ToString("0.00", CultureInfo.InvariantCulture) ?? "");
        amount.LostFocus += (_, _) => NormalizeAmount(amount);
        var account = new ComboBox
        {
            ItemsSource = accounts,
            SelectedItem = accounts.FirstOrDefault(a => a.Id == (existing?.AccountId ?? _lastAccountId)) ?? accounts[0],
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var destination = new ComboBox
        {
            ItemsSource = accounts,
            SelectedItem = accounts.FirstOrDefault(a => a.Id == existing?.DestinationId),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var categoryPaths = _snapshot.Categories
            .Where(c => !c.Archived || c.Id == existing?.CategoryId)
            .Select(c => c.Path)
            .ToList();
        var category = new AutoCompleteBox
        {
            ItemsSource = categoryPaths,
            Text = _snapshot.Categories.FirstOrDefault(c => c.Id == existing?.CategoryId)?.Path ?? "",
            FilterMode = AutoCompleteFilterMode.ContainsOrdinal,
            MinimumPrefixLength = 1,
            PlaceholderText = "Type or select a category",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var memo = Input(existing?.Memo ?? "");
        var toField = Field("Destination account", destination);
        var categoryField = Field("Category / subcategory", category);
        void UpdateFields()
        {
            var transfer = kind.SelectedItem is TransactionKind.Transfer;
            toField.IsVisible = transfer;
            categoryField.IsVisible = !transfer;
        }
        kind.SelectionChanged += (_, _) => UpdateFields();
        UpdateFields();
        Action? continueAfterSave = null;
        if (entry is null)
        {
            continueAfterSave = () =>
            {
                var path = category.Text?.Trim();
                if (!string.IsNullOrEmpty(path) && !categoryPaths.Contains(path, StringComparer.OrdinalIgnoreCase))
                {
                    categoryPaths.Add(path);
                    category.ItemsSource = categoryPaths.ToArray();
                    category.Text = path;
                }

                description.Text = "";
                amount.Text = "";
                memo.Text = "";
                amount.Focus();
            };
        }

        await EditDialog(entry is null ? "Add transaction" : "Edit transaction",
            [Field("Type", kind), Field("Date", date), categoryField, Field("Amount (positive)", amount), Field("Account", account), toField, Field("Description", description), Field("Notes", memo)],
            () =>
            {
                NormalizeAmount(amount);
                var type = (TransactionKind)kind.SelectedItem!;
                var draft = new TransactionDraft(type, ParseDate(date), description.Text ?? "", ParseMoney(amount), ((Account)account.SelectedItem!).Id,
                    type == TransactionKind.Transfer ? (destination.SelectedItem as Account)?.Id : null,
                    Memo: memo.Text ?? "");
                var categoryPath = type == TransactionKind.Transfer ? null : category.Text;
                _lastAccountId = draft.AccountId;
                return () => _store.SaveTransactionWithCategoryPath(entry?.Id, draft, categoryPath);
            }, initialFocus: category, onSaveAndContinue: continueAfterSave);
    }

    private async Task RemoveTransaction(LedgerEntry entry) => await EditDialog("Remove transaction",
        [HistoryRow(entry.Draft.Date.ToString("dd MMM yyyy", CultureInfo.CurrentCulture),
            string.IsNullOrWhiteSpace(entry.Draft.Description) ? "(No description)" : entry.Draft.Description,
            entry.CategoryPath ?? "—",
            entry.DestinationName is null ? entry.AccountName : $"{entry.AccountName} → {entry.DestinationName}",
            (entry.Draft.Kind == TransactionKind.Expense ? "− " : entry.Draft.Kind == TransactionKind.Income ? "+ " : "↔ ") + AmountText(entry.Draft.Amount),
            false, entry.Draft.Kind), Text("Remove this transaction? For a transfer, both account movements will be removed together.")],
        () => () => _store.DeleteTransaction(entry.Id), "Remove");

    private sealed record HistoryItem(HistoryHit Hit)
    {
        public override string ToString()
        {
            var entry = Hit.Entry;
            return $"{entry.Draft.Date:yyyy-MM-dd} · {entry.Draft.Description} · {entry.Draft.Kind} · {AmountText(entry.Draft.Amount)} · {entry.AccountName}";
        }
    }
}
