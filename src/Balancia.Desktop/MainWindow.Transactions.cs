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
    private static ListBox HistoryList(IReadOnlyList<HistoryHit> hits, bool compact) => new()
    {
        ItemsSource = hits.Select(hit => new HistoryItem(hit)).ToArray(),
        ItemTemplate = new FuncDataTemplate<HistoryItem>((item, _) => HistoryRow(item.Hit, compact), true),
        HorizontalAlignment = HorizontalAlignment.Stretch,
        Background = Brushes.Transparent,
        BorderThickness = new Thickness(0)
    };

    private static Grid HistoryRow(HistoryHit hit, bool compact)
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
            string.IsNullOrWhiteSpace(entry.Draft.Description) ? "(No description)" : entry.Draft.Description,
            entry.CategoryPath ?? "—",
            entry.DestinationName is null ? entry.AccountName : $"{entry.AccountName} → {entry.DestinationName}",
            sign + AmountText(new Money(Math.Abs(amount.Centimes))), false, compact, entry.Draft.Kind);
    }

    private static Grid HistoryRow(string date, string description, string category, string account, string amount,
        bool header, bool compact, TransactionKind? kind = null)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions(compact ? "98,*,200,80,80" : "110,*,255,145,140"),
            MinHeight = header ? 31 : 39
        };
        var values = new[] { date, description, category, account, amount };

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
                FontWeight = !header && i == 4 ? FontWeight.Bold : FontWeight.Normal,
                Foreground = Brush.Parse(header ? "#71838D" : i == 4 ? amountColor : "#263C48"),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextAlignment = i == 4 ? TextAlignment.Right : TextAlignment.Left,
                Margin = new Thickness(0, 0, 5, 0)
            };
            AddColumn(row, cell, i);
        }
        return row;
    }

    private async Task OpenTransactionFromOverview(LedgerEntry entry)
    {
        await Run(async () =>
        {
            var offset = await Task.Run(() => _store.FindHistoryOffset(entry.Id));
            _historyFilter = new();
            _historyCursors.Clear();
            _historyOffset = offset / HistoryPageSize * HistoryPageSize;
            _historyUseOffsetPaging = true;
            _pendingHistoryId = entry.Id;
            _page = "Transactions";
            await Refresh();
        });
    }

    private void RenderTransactions(LedgerSnapshot snapshot)
    {
        var layout = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*"),
            RowSpacing = 11
        };
        AddRow(layout, QuietText("Newest first · Filters combine · Date and amount endpoints are inclusive", 12), 0);
        var search = Input(_historyFilter.Description ?? "");
        search.Width = 880;
        search.HorizontalAlignment = HorizontalAlignment.Left;
        var accountChoices = new List<Choice<string?>> { new(null, "All accounts") };
        accountChoices.AddRange(snapshot.Accounts.Select(a => new Choice<string?>(a.Id, a.Name)));
        var accountFilter = new ComboBox
        {
            ItemsSource = accountChoices,
            SelectedItem = accountChoices.FirstOrDefault(c => c.Value == _historyFilter.AccountId) ?? accountChoices[0],
            Width = 195
        };
        var typeChoices = new List<Choice<TransactionKind?>> { new(null, "All types") };
        typeChoices.AddRange(Enum.GetValues<TransactionKind>().Select(k => new Choice<TransactionKind?>(k, k.ToString())));
        var typeFilter = new ComboBox
        {
            ItemsSource = typeChoices,
            SelectedItem = typeChoices.FirstOrDefault(c => c.Value == _historyFilter.Kind) ?? typeChoices[0],
            Width = 160
        };
        var categoryChoices = new List<Choice<string?>> { new(null, "All categories") };
        categoryChoices.AddRange(snapshot.Categories.Select(c => new Choice<string?>(c.Id, c.Path)));
        var categoryFilter = new ComboBox
        {
            ItemsSource = categoryChoices,
            SelectedItem = categoryChoices.FirstOrDefault(c => c.Value == _historyFilter.CategoryId) ?? categoryChoices[0],
            Width = 240
        };
        var from = DateInput(_historyFilter.From);
        from.Width = 170;
        var to = DateInput(_historyFilter.To);
        to.Width = 170;
        var minimum = Input(_historyFilter.Minimum?.Francs.ToString("0.00", CultureInfo.InvariantCulture) ?? "");
        minimum.Width = 130;
        var maximum = Input(_historyFilter.Maximum?.Francs.ToString("0.00", CultureInfo.InvariantCulture) ?? "");
        maximum.Width = 130;
        var filters = new StackPanel { Spacing = 10 };
        filters.Children.Add(Heading("Search and filters", 13));
        filters.Children.Add(Field("Description contains", search));
        filters.Children.Add(Row(Field("Account", accountFilter), Field("Type", typeFilter), Field("Category / subcategory", categoryFilter)));
        filters.Children.Add(Row(Field("From", from), Field("To", to), Field("Min amount", minimum), Field("Max amount", maximum)));
        async Task ApplyFilters()
        {
            await Run(async () =>
            {
                var proposed = new HistoryFilter(search.Text, ((Choice<string?>)accountFilter.SelectedItem!).Value,
                    ((Choice<TransactionKind?>)typeFilter.SelectedItem!).Value, ((Choice<string?>)categoryFilter.SelectedItem!).Value,
                    from.SelectedDate is { } fromDate ? DateOnly.FromDateTime(fromDate.DateTime) : null,
                    to.SelectedDate is { } toDate ? DateOnly.FromDateTime(toDate.DateTime) : null,
                    OptionalMoney(minimum), OptionalMoney(maximum));
                if (proposed.From > proposed.To || proposed.Minimum?.Centimes < 0 || proposed.Maximum?.Centimes < 0 ||
                    proposed.Minimum is { } min && proposed.Maximum is { } max && min.Centimes > max.Centimes)
                {
                    throw new ArgumentException("Check the date and amount range endpoints.");
                }

                _historyFilter = proposed;
                _historyOffset = 0;
                _historyCursors.Clear();
                _historyUseOffsetPaging = false;
                _pendingHistoryId = null;
                await Refresh();
            });
        }
        search.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                await ApplyFilters();
            }
        };
        filters.Children.Add(Row(ActionButton("Apply filters", ApplyFilters),
            ActionButton("Clear filters", async () =>
            {
                _historyFilter = new();
                _historyOffset = 0;
                _historyCursors.Clear();
                _historyUseOffsetPaging = false;
                _pendingHistoryId = null;
                await Run(Refresh);
            })));
        AddRow(layout, Panel(filters), 1);

        var page = _historyPage!;
        var entries = HistoryList(page.Hits, false);
        var body = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto"),
            RowSpacing = 8
        };
        var toolbar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        toolbar.Children.Add(Heading($"Transaction history · {page.TotalCount:N0}", 13));
        AddColumn(toolbar, Row
            (
                ActionButton("Edit selected", () => entries.SelectedItem is HistoryItem item ? EditTransaction(item.Hit.Entry) : SelectFirst()),
                ActionButton("Remove selected", () => entries.SelectedItem is HistoryItem item ? RemoveTransaction(item.Hit.Entry) : SelectFirst())),
            1);
        AddRow(body, toolbar, 0);
        AddRow(body, QuietText($"Showing {(_historyOffset == 0 && page.Hits.Count == 0 ? 0 : _historyOffset + 1):N0}–{(_historyOffset + page.Hits.Count):N0} of {page.TotalCount:N0}", 11), 1);
        AddRow(body, HistoryRow("Date", "Description", "Category", "Account", "Amount", true, false), 2);

        if (page.Hits.Count == 0)
        {
            AddRow(body, QuietText("No matching transactions.", 13), 3);
        }
        else
        {
            AddRow(body, entries, 3);
        }

        AddRow(body, Row(ActionButton("Previous page", async () =>
            {
                if (_historyUseOffsetPaging)
                {
                    if (_historyOffset > 0)
                    {
                        _historyOffset = Math.Max(0, _historyOffset - HistoryPageSize);
                        await Run(Refresh);
                    }
                }
                else if (_historyCursors.Count > 0)
                {
                    _historyCursors.RemoveAt(_historyCursors.Count - 1);
                    _historyOffset = Math.Max(0, _historyOffset - HistoryPageSize);
                    await Run(Refresh);
                }
            }),
            ActionButton("Next page", async () =>
            {
                if (_historyOffset + page.Hits.Count < page.TotalCount && page.Hits.Count > 0)
                {
                    if (!_historyUseOffsetPaging)
                    {
                        var last = page.Hits[^1].Entry;
                        _historyCursors.Add((last.Draft.Date, last.Id));
                    }
                    _historyOffset += page.Hits.Count;
                    await Run(Refresh);
                }
            })), 4);
        var historyCard = Panel(body);
        historyCard.MaxWidth = 1280;
        historyCard.HorizontalAlignment = HorizontalAlignment.Stretch;
        AddRow(layout, historyCard, 2);
        ResponsiveBody.Content = layout;
        if (_pendingHistoryId is { } pending)
        {
            var selected = (entries.ItemsSource as HistoryItem[])?.FirstOrDefault(item => item.Hit.Entry.Id == pending);
            if (selected is not null)
            {
                entries.SelectedItem = selected;
                Dispatcher.UIThread.Post(() => entries.ScrollIntoView(selected));
            }
            _pendingHistoryId = null;
        }
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
        var date = Input((existing?.Date ?? _displayDate).ToString("yyyy-MM-dd"));
        var description = Input(existing?.Description ?? "");
        var amount = Input(existing?.Amount.Francs.ToString("0.00", CultureInfo.InvariantCulture) ?? "");
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
        var choices = new List<Choice<string?>> { new(null, "Uncategorized") };
        choices.AddRange(_snapshot.Categories.Where(c => !c.Archived || c.Id == existing?.CategoryId).Select(c => new Choice<string?>(c.Id, c.ToString())));
        var category = new ComboBox
        {
            ItemsSource = choices,
            SelectedItem = choices.FirstOrDefault(c => c.Value == existing?.CategoryId) ?? choices[0],
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var categorySearch = Input("");
        categorySearch.PlaceholderText = "Type to narrow categories";
        categorySearch.TextChanged += (_, _) =>
        {
            var selected = category.SelectedItem as Choice<string?>;
            var visible = choices.Where(c => c.Value is null || c.Label.Contains(categorySearch.Text ?? "", StringComparison.OrdinalIgnoreCase)).ToArray();
            category.ItemsSource = visible;
            category.SelectedItem = visible.FirstOrDefault(c => c.Value == selected?.Value) ?? visible[0];
        };
        var memo = Input(existing?.Memo ?? "");
        var toField = Field("Destination account", destination);
        var categoryField = Field("Category / subcategory", new StackPanel
        {
            Spacing = 5,
            Children = { categorySearch, category }
        });
        void UpdateFields()
        {
            var transfer = kind.SelectedItem is TransactionKind.Transfer;
            toField.IsVisible = transfer;
            categoryField.IsVisible = !transfer;
        }
        kind.SelectionChanged += (_, _) => UpdateFields();
        UpdateFields();
        await EditDialog(entry is null ? "Add transaction" : "Edit transaction",
            [Field("Type", kind), Field("Date (YYYY-MM-DD)", date), Field("Description", description), Field("Amount (positive)", amount), Field("Account", account), toField, categoryField, Field("Notes", memo)],
            () =>
            {
                var type = (TransactionKind)kind.SelectedItem!;
                var draft = new TransactionDraft(type, ParseDate(date), description.Text ?? "", ParseMoney(amount), ((Account)account.SelectedItem!).Id,
                    type == TransactionKind.Transfer ? (destination.SelectedItem as Account)?.Id : null,
                    type == TransactionKind.Transfer ? null : ((Choice<string?>)category.SelectedItem!).Value, memo.Text ?? "");
                _lastAccountId = draft.AccountId;
                return () => _store.SaveTransaction(entry?.Id, draft);
            });
    }

    private async Task RemoveTransaction(LedgerEntry entry) => await EditDialog("Remove transaction",
        [Text(EntryText(entry)), Text("Remove this transaction? For a transfer, both account movements will be removed together.")],
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
