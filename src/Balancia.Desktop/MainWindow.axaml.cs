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

public partial class MainWindow : Window
{
    private enum OverviewPeriod
    {
        All, ThisWeek, ThisMonth, ThisYear, Custom
    }
    private const int HistoryPageSize = 1000;
    private readonly LedgerStore _store;
    private LedgerSnapshot? _snapshot;
    private HistoryPage? _historyPage;
    private HistoryPage? _overviewHistory;
    private IReadOnlyList<RecurringReminder> _reminders = [];
    private HistoryFilter _historyFilter = new();
    private int _historyOffset;
    private readonly List<(DateOnly Date, string Id)> _historyCursors = [];
    private bool _historyUseOffsetPaging;
    private string? _pendingHistoryId;
    private string _page = "Overview";
    private bool _busy;
    private DateOnly _displayDate = DateOnly.FromDateTime(DateTime.Today);
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(30) };
    private string? _lastAccountId;
    private OverviewPeriod _overviewPeriod = OverviewPeriod.ThisMonth;
    private DateOnly? _customFrom;
    private DateOnly? _customTo;

    public MainWindow()
    {
        InitializeComponent();
        var args = Environment.GetCommandLineArgs();
        var directoryArg = Array.IndexOf(args, "--data-dir");
        var directory = directoryArg >= 0 && directoryArg + 1 < args.Length
            ? Path.GetFullPath(args[directoryArg + 1])
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Balancia");
        _store = new LedgerStore(Path.Combine(directory, "balancia.db"));
        if (directoryArg >= 0)
        {
            Title = "Balancia — Separate data folder";
        }

        Opened += async (_, _) => await Run(async () =>
        {
            await Task.Run(() => { Directory.CreateDirectory(directory); _store.Initialize(); });
            await Refresh();
        });
        _timer.Tick += async (_, _) =>
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            if (!_busy && today != _displayDate)
            {
                await Run(Refresh);
            }
        };
        Opened += (_, _) => _timer.Start();
        Closed += (_, _) => _timer.Stop();
    }

    private (DateOnly? From, DateOnly? To) OverviewRange() => _overviewPeriod switch
    {
        OverviewPeriod.All => (null, null),
        OverviewPeriod.ThisWeek => WeekRange(_displayDate),
        OverviewPeriod.ThisMonth => (new DateOnly(_displayDate.Year, _displayDate.Month, 1),
            new DateOnly(_displayDate.Year, _displayDate.Month, 1).AddMonths(1).AddDays(-1)),
        OverviewPeriod.ThisYear => (new DateOnly(_displayDate.Year, 1, 1), new DateOnly(_displayDate.Year, 12, 31)),
        _ => (_customFrom, _customTo)
    };

    private static (DateOnly From, DateOnly To) WeekRange(DateOnly date)
    {
        var daysSinceMonday = ((int)date.DayOfWeek + 6) % 7;
        var monday = date.AddDays(-daysSinceMonday);
        return (monday, monday.AddDays(6));
    }

    private string OverviewPeriodLabel() => _overviewPeriod switch
    {
        OverviewPeriod.All => "All dates",
        OverviewPeriod.ThisWeek => "This week",
        OverviewPeriod.ThisMonth => _displayDate.ToString("MMMM yyyy", CultureInfo.CurrentCulture),
        OverviewPeriod.ThisYear => _displayDate.Year.ToString(CultureInfo.CurrentCulture),
        _ => $"{_customFrom:dd MMM yyyy} – {_customTo:dd MMM yyyy}"
    };

    private void RenderOverview(LedgerSnapshot snapshot)
    {
        var label = OverviewPeriodLabel();
        var layout = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*"),
            RowSpacing = 11
        };
        AddRow(layout, QuietText($"{label} · All accounts", 12), 0);
        var filterBox = new StackPanel { Spacing = 8 };
        var filters = new WrapPanel();
        filters.Children.Add(QuietText("PERIOD", 11));
        foreach (var (period, title) in new[] { (OverviewPeriod.All, "All"), (OverviewPeriod.ThisWeek, "This Week"),
                     (OverviewPeriod.ThisMonth, "This Month"), (OverviewPeriod.ThisYear, "This Year"), (OverviewPeriod.Custom, "Custom") })
        {
            var button = ActionButton(title, async () =>
            {
                _overviewPeriod = period;
                if (period == OverviewPeriod.Custom && (_customFrom is null || _customTo is null))
                {
                    _customFrom = new DateOnly(_displayDate.Year, _displayDate.Month, 1);
                    _customTo = _displayDate;
                }
                await Run(Refresh);
            });
            button.Margin = new Thickness(5, 0, 0, 0);
            if (_overviewPeriod == period)
            {
                button.Background = Brush.Parse("#D8ECF3");
                button.Foreground = Brush.Parse("#1C627E");
                button.FontWeight = FontWeight.SemiBold;
            }
            filters.Children.Add(button);
        }
        filterBox.Children.Add(filters);
        if (_overviewPeriod == OverviewPeriod.Custom)
        {
            var from = DateInput(_customFrom);
            var to = DateInput(_customTo);
            from.Width = to.Width = 150;
            var customDates = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto"),
                ColumnSpacing = 10
            };
            AddColumn(customDates, Field("From", from), 0);
            AddColumn(customDates, Field("To", to), 1);
            var apply = ActionButton("Apply", async () =>
            {
                var first = from.SelectedDate is { } f ? DateOnly.FromDateTime(f.DateTime) : (DateOnly?)null;
                var last = to.SelectedDate is { } t ? DateOnly.FromDateTime(t.DateTime) : (DateOnly?)null;
                if (first is null || last is null || first > last)
                {
                    Status.Text = "Choose a valid From and To date.";
                    return;
                }
                _customFrom = first;
                _customTo = last;
                await Run(Refresh);
            });
            apply.VerticalAlignment = VerticalAlignment.Bottom;
            AddColumn(customDates, apply, 2);
            filterBox.Children.Add(customDates);
        }
        AddRow(layout, Panel(filterBox), 1);

        var summary = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("1.18*,*,*"),
            ColumnSpacing = 10
        };
        var accountRows = new StackPanel { Spacing = 0 };
        accountRows.Children.Add(Heading("NET WORTH · ALL ACCOUNTS", 11));
        foreach (var account in snapshot.Accounts)
        {
            accountRows.Children.Add(TwoColumn(account.ToString(), AmountText(account.Balance), 12,
                BalanceColor(account.Balance)));
        }

        if (snapshot.Accounts.Count == 0)
        {
            accountRows.Children.Add(QuietText("No accounts yet", 12));
        }

        var total = TwoColumn("Total net worth", AmountText(snapshot.NetWorth), 16,
            BalanceColor(snapshot.NetWorth));
        total.Margin = new Thickness(0, 9, 0, 0);
        accountRows.Children.Add(total);
        AddColumn(summary, Panel(accountRows), 0);
        AddColumn(summary, Metric("INCOME", snapshot.MonthlyIncome, label, "#2C8B6D"), 1);
        AddColumn(summary, Metric("EXPENSES", snapshot.MonthlyExpenses, label, "#B95D4D"), 2);
        AddRow(layout, summary, 2);

        var lower = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("1.7*,0.85*"),
            ColumnSpacing = 11
        };
        AddColumn(lower, HistoryPanel(), 0);
        var right = new StackPanel { Spacing = 11 };
        right.Children.Add(CategoriesPanel(snapshot));
        right.Children.Add(RemindersPanel());
        AddColumn(lower, right, 1);
        AddRow(layout, lower, 3);
        ResponsiveBody.Content = layout;
    }

    private static void AddColumn(Grid grid, Control control, int column)
    {
        Grid.SetColumn(control, column);
        grid.Children.Add(control);
    }

    private static void AddRow(Grid grid, Control control, int row)
    {
        Grid.SetRow(control, row);
        grid.Children.Add(control);
    }

    private static Border Panel(Control content) => new()
    {
        Background = Brushes.White,
        BorderBrush = Brush.Parse("#DCE6EB"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(6),
        Padding = new Thickness(15),
        Child = content
    };

    private static TextBlock QuietText(string content, double size) => new()
    {
        Text = content,
        FontSize = size,
        Foreground = Brush.Parse("#71838D"),
        VerticalAlignment = VerticalAlignment.Center
    };

    private static TextBlock Heading(string content, double size) => new()
    {
        Text = content,
        FontSize = size,
        FontWeight = FontWeight.SemiBold,
        Foreground = Brush.Parse("#263C48"),
        Margin = new Thickness(0, 0, 0, 10)
    };

    private static IBrush BalanceColor(Money amount) => Brush.Parse(amount.Centimes > 0 ? "#2C8B6D" : "#B95D4D");

    private static Grid TwoColumn(string left, string right, double size, IBrush? valueColor = null)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(0, 4)
        };
        row.Children.Add(new TextBlock
        {
            Text = left,
            FontSize = size,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        var value = new TextBlock
        {
            Text = right,
            FontSize = size,
            FontWeight = FontWeight.SemiBold,
            Foreground = valueColor ?? Brush.Parse("#263C48"),
            Margin = new Thickness(8, 0, 0, 0)
        };
        AddColumn(row, value, 1);
        return row;
    }

    private static Border Metric(string title, Money value, string scope, string valueColor) => Panel(new StackPanel
    {
        Spacing = 18,
        Children = { Heading(title, 11), new TextBlock {
Text = AmountText(value), FontSize = 23, FontWeight = FontWeight.SemiBold,
            Foreground = Brush.Parse(valueColor)
}, QuietText(scope, 11) }

    });

    private Border HistoryPanel()
    {
        var body = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*"),
            RowSpacing = 2
        };
        AddRow(body, SectionHeader($"Transaction history · {_overviewHistory?.TotalCount:N0}", "See all →", () => Navigate("Transactions")), 0);
        AddRow(body, HistoryRow("Date", "Description", "Category", "Account", "Amount", true, true), 1);
        if (_overviewHistory is not { Hits.Count: > 0 })
        {
            AddRow(body, QuietText("No transactions in this period.", 13), 2);
        }
        else
        {
            var entries = HistoryList(_overviewHistory.Hits, true);
            entries.DoubleTapped += async (_, _) =>
            {
                if (entries.SelectedItem is HistoryItem item)
                {
                    await OpenTransactionFromOverview(item.Hit.Entry);
                }
            };
            AddRow(body, entries, 2);
        }
        return Panel(body);
    }

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

    private static Grid SectionHeader(string title, string link, Func<Task> action)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(0, 0, 0, 10)
        };
        row.Children.Add(Heading(title, 13));
        var button = ActionButton(link, action);
        button.FontSize = 10;
        button.Padding = new Thickness(4);
        button.Background = Brushes.Transparent;
        button.Foreground = Brush.Parse("#3989A7");
        AddColumn(row, button, 1);
        return row;
    }

    private Border CategoriesPanel(LedgerSnapshot snapshot)
    {
        var body = new StackPanel
        {
            Spacing = 1
        };

        body.Children.Add(SectionHeader("Largest expense categories", "View all →", () => Navigate("Categories")));

        if (snapshot.LargestCategories.Count == 0)
        {
            body.Children.Add(QuietText("No expenses in this period.", 12));
        }

        var maximum = snapshot.LargestCategories.FirstOrDefault()?.Amount.Centimes ?? 1;

        foreach (var category in snapshot.LargestCategories)
        {
            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("75,*,80"),
                MinHeight = 27
            };

            row.Children.Add(new TextBlock
            {
                Text = category.Name,
                FontSize = 11,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center

            });

            var bar = new ProgressBar
            {
                Minimum = 0,
                Maximum = maximum,
                Value = category.Amount.Centimes,
                Height = 7,
                Foreground = Brush.Parse("#3989A7"),
                Background = Brush.Parse("#E7F2F6"),
                VerticalAlignment = VerticalAlignment.Center
            };

            AddColumn(row, bar, 1);
            var value = new TextBlock
            {
                Text = AmountText(category.Amount),
                FontSize = 11,
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeight.SemiBold
            };

            AddColumn(row, value, 2);
            body.Children.Add(row);
        }
        return Panel(body);
    }

    private Border RemindersPanel()
    {
        var body = new StackPanel
        {
            Spacing = 1
        };

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(0, 0, 0, 7)
        };

        header.Children.Add(Heading("Upcoming payments", 13));

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 3
        };

        var recurring = new ListBox
        {
            ItemsSource = _reminders.Take(5).Select(r => new Choice<RecurringReminder>(r, ReminderText(r))).ToArray(),
            MinHeight = _reminders.Count == 0 ? 0 : 45,
            MaxHeight = 175,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            ItemTemplate = new FuncDataTemplate<Choice<RecurringReminder>>((choice, _) =>
            {
                var row = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                    MinHeight = 29
                };
                var reminder = choice.Value;

                row.Children.Add(new TextBlock
                {
                    Text = $"{reminder.Occurrence:dd MMM} · {reminder.Template.Description}",
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis

                });

                var amount = new TextBlock
                {
                    Text = AmountText(reminder.Template.IndicativeAmount),
                    FontSize = 12,
                    FontWeight = FontWeight.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center
                };

                AddColumn(row, amount, 1);
                return row;
            },
            true)
        };

        var add = ActionButton("+", () => EditRecurring(null));
        add.Width = 30;
        add.Padding = new Thickness(0);
        add.FontSize = 18;

        var remove = ActionButton("🗑", () => DeleteSelectedRecurring(recurring));
        remove.Width = 30;
        remove.Padding = new Thickness(0);
        remove.FontSize = 14;

        ToolTip.SetTip(add, "Add recurring payment");
        ToolTip.SetTip(remove, "Delete selected recurring payment");

        actions.Children.Add(add);
        actions.Children.Add(remove);
        AddColumn(header, actions, 1);
        body.Children.Add(header);

        recurring.DoubleTapped += async (_, _) =>
        {
            if (recurring.SelectedItem is Choice<RecurringReminder> selected)
            {
                await EditRecurring(selected.Value);
            }
        };

        if (_reminders.Count == 0)
        {
            body.Children.Add(QuietText("No recurring payment templates.", 12));
        }
        else
        {
            body.Children.Add(recurring);
        }

        return Panel(body);
    }

    private async Task DeleteSelectedRecurring(ListBox recurring)
    {
        if (recurring.SelectedItem is not Choice<RecurringReminder> choice)
        {
            await SelectFirst();
            return;
        }
        var dialog = new Window
        {
            Title = "Delete recurring payment",
            Width = 430,
            Height = 210,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var cancel = new Button
        {
            Content = "Cancel",
            IsCancel = true
        };
        var remove = new Button
        {
            Content = "Delete",
            IsDefault = true
        };
        remove.Click += async (_, _) =>
        {
            remove.IsEnabled = false;
            try
            {
                await Task.Run(() => _store.DeleteRecurringTemplate(choice.Value.Template.Id));
                dialog.Close();
                await Run(Refresh);
            }
            catch (Exception ex) { await ShowErrorDialog("Balancia", FriendlyError(ex)); remove.IsEnabled = true; }
        };
        cancel.Click += (_, _) => dialog.Close();
        dialog.Content = new StackPanel
        {
            Spacing = 14,
            Margin = new Thickness(22),
            Children =
        { Text($"Delete '{choice.Value.Template.Description}'?"), Row(remove, cancel) }
        };
        await dialog.ShowDialog(this);
    }

    private async Task Refresh()
    {
        _displayDate = DateOnly.FromDateTime(DateTime.Today);
        var (from, to) = OverviewRange();
        _snapshot = await Task.Run(() => _page == "Overview" ? _store.ReadDesktopSnapshotForPeriod(from, to) : _store.ReadDesktopSnapshot());
        _reminders = await Task.Run(() => _store.ReadRecurringReminders());
        if (_page == "Transactions")
        {
            _historyPage = await Task.Run(() => _historyUseOffsetPaging ? _store.ReadHistory(_historyFilter, _historyOffset, HistoryPageSize) :
                _historyCursors.Count == 0 ? _store.ReadHistory(_historyFilter, 0, HistoryPageSize) :
                _store.ReadHistoryAfter(_historyFilter, _historyCursors[^1].Date, _historyCursors[^1].Id, HistoryPageSize));
        }

        if (_page == "Overview")
        {
            _overviewHistory = await Task.Run(() => _store.ReadAllHistory(new HistoryFilter(From: from, To: to)));
        }

        Render();
    }

    private async Task Run(Func<Task> action)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        PageBody.IsEnabled = false;
        ResponsiveBody.IsEnabled = false;
        Navigation.IsEnabled = false;
        HeaderActions.IsEnabled = false;
        Status.Text = "Working…";
        try
        {
            await action();
            Status.Text = "Saved locally";
        }
        catch (Exception ex) { Status.Text = FriendlyError(ex); await ShowErrorDialog("Balancia", FriendlyError(ex)); }
        finally { _busy = false; PageBody.IsEnabled = true; ResponsiveBody.IsEnabled = true; Navigation.IsEnabled = true; HeaderActions.IsEnabled = true; }
    }

    private static string FriendlyError(Exception ex) => ex switch
    {
        SqliteException { SqliteErrorCode: 19 } => "This change conflicts with existing data. Check names and referenced accounts/categories.",
        SqliteException => "The database could not be read or saved. Close other Balancia windows and try again.",
        OverflowException => "This amount or resulting total is outside the supported range.",
        FormatException => "Check the date (YYYY-MM-DD) and amount (for example 12.50).",
        IOException or UnauthorizedAccessException => "The local data folder is unavailable or not writable.",
        InvalidDataException => ex.Message,
        ArgumentException or InvalidOperationException => ex.Message,
        _ => "The operation failed. Your entered values have been kept; try again."
    };

    private async Task ShowErrorDialog(string title, string message)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 520,
            Height = 260,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var close = new Button
        {
            Content = "Close",
            IsDefault = true,
            IsCancel = true
        };
        close.Click += (_, _) => dialog.Close();
        dialog.Content = new StackPanel
        {
            Spacing = 16,
            Margin = new Thickness(24),
            Children =
        { new TextBlock {
Text = "The operation could not be completed.", FontSize = 20, FontWeight = FontWeight.SemiBold
},
          new TextBlock {
Text = message, TextWrapping = TextWrapping.Wrap
}, close }
        };
        await dialog.ShowDialog(this);
    }

    private async void ShowOverview(object? sender, RoutedEventArgs e) => await Navigate("Overview");
    private async void ShowAccounts(object? sender, RoutedEventArgs e) => await Navigate("Accounts");
    private async void ShowCategories(object? sender, RoutedEventArgs e) => await Navigate("Categories");
    private async void ShowTransactions(object? sender, RoutedEventArgs e) => await Navigate("Transactions");
    private async Task Navigate(string page)
    {
        if (_page == page)
        {
            return;
        }
        _page = page;
        await Run(Refresh);
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

    private void Render()
    {
        PageTitle.Text = _page;
        foreach (var child in Navigation.Children.OfType<Button>())
        {
            child.Classes.Set("selected", Equals(child.Content, _page));
        }
        HeaderActions.Children.Clear();
        if (_page == "Overview")
        {
            HeaderActions.Children.Add(ActionButton("Search", () => Navigate("Transactions")));
            var add = ActionButton("+ Transaction", () => EditTransaction(null));
            add.Background = Brush.Parse("#3989A7");
            add.Foreground = Brushes.White;
            HeaderActions.Children.Add(add);
        }
        else if (_page == "Transactions")
        {
            HeaderActions.Children.Add(ActionButton("Import CSV", ImportCsv));
            HeaderActions.Children.Add(ActionButton("Export CSV", ExportCsv));
            HeaderActions.Children.Add(ActionButton("Export snapshot", ExportSnapshot));
            HeaderActions.Children.Add(ActionButton("Restore snapshot", RestoreSnapshot));
            var add = ActionButton("+ Transaction", () => EditTransaction(null));
            add.Background = Brush.Parse("#3989A7");
            add.Foreground = Brushes.White;
            HeaderActions.Children.Add(add);
        }
        var responsive = _page is "Overview" or "Transactions" or "Categories";
        PageScrollViewer.IsVisible = !responsive;
        ResponsiveBody.IsVisible = responsive;
        ResponsiveBody.Content = null;
        PageBody.Spacing = 18;
        PageBody.Children.Clear();
        if (_snapshot is not { } s)
        {
            var message = Text("The ledger could not be loaded. Check the message below and restart after resolving it.");
            if (responsive)
            {
                ResponsiveBody.Content = message;
            }
            else
            {
                PageBody.Children.Add(message);
            }

            return;
        }
        switch (_page)
        {
            case "Overview":
                RenderOverview(s);
                break;
            case "Accounts":
                PageBody.Children.Add(Text("Set a dated opening balance. Archive accounts to stop new entries without losing history."));
                var accounts = new ListBox
                {
                    ItemsSource = s.Accounts.Select(a => new Choice<Account>(a, $"{a}    {AmountText(a.Balance)}")).ToArray(),
                    MinHeight = 100,
                    MaxHeight = 300
                };
                PageBody.Children.Add(accounts);
                PageBody.Children.Add(Row(ActionButton("Add account", () => EditAccount(null)), ActionButton("Edit selected account", () => accounts.SelectedItem is Choice<Account> a ? EditAccount(a.Value) : SelectFirst())));
                break;
            case "Categories":
                RenderCategories(s);
                break;
            case "Transactions":
                RenderTransactions(s);
                break;
        }
    }

    private void RenderCategories(LedgerSnapshot snapshot)
    {
        var layout = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*"),
            RowSpacing = 11
        };
        AddRow(layout, Text("Choose an optional parent for a subcategory. Archiving a parent also archives its children."), 0);
        var categories = new ListBox
        {
            ItemsSource = snapshot.Categories,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0)
        };
        AddRow(layout, Row(ActionButton("Add category", () => EditCategory(null)),
            ActionButton("Edit selected category", () => categories.SelectedItem is Category c ? EditCategory(c) : SelectFirst())), 1);
        AddRow(layout, Panel(categories), 2);
        ResponsiveBody.Content = layout;
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
        AddColumn(toolbar, Row(ActionButton("Edit selected", () => entries.SelectedItem is HistoryItem item ? EditTransaction(item.Hit.Entry) : SelectFirst()),
            ActionButton("Remove selected", () => entries.SelectedItem is HistoryItem item ? RemoveTransaction(item.Hit.Entry) : SelectFirst())), 1);
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

    private Task SelectFirst()
    {
        Status.Text = "Select a row first.";
        return Task.CompletedTask;
    }
    private static string ReminderText(RecurringReminder reminder) =>
        $"{(reminder.Overdue ? "OVERDUE · " : "")}{reminder.Occurrence:yyyy-MM-dd} · {reminder.Template.Description} · indicative {AmountText(reminder.Template.IndicativeAmount)} · every {reminder.Template.IntervalMonths} month(s)";

    private async Task EditRecurring(RecurringReminder? reminder)
    {
        var template = reminder?.Template;
        var description = Input(template?.Description ?? "");
        var date = Input((template?.ExpectedDate ?? _displayDate).ToString("yyyy-MM-dd"));
        var amount = Input((template?.IndicativeAmount.Francs ?? 0).ToString("0.00", CultureInfo.InvariantCulture));
        var interval = Input((template?.IntervalMonths ?? 1).ToString(CultureInfo.InvariantCulture));
        await EditDialog(template is null ? "Add recurring template" : "Edit recurring template",
            [Field("Description (exact match)", description), Field("Expected date (YYYY-MM-DD)", date), Field("Indicative amount", amount), Field("Repeat every N months", interval)],
            () =>
            {
                var values = (description.Text ?? "", ParseDate(date), ParseMoney(amount),
                    int.Parse(interval.Text ?? "", CultureInfo.InvariantCulture));
                return () => _store.SaveRecurringTemplate(template?.Id, values.Item1, values.Item2, values.Item3, values.Item4, template?.Archived ?? false);
            });
    }
    private async Task ImportCsv()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose transactions CSV",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("CSV") { Patterns = ["*.csv"] }]

        });
        if (files.Count == 0)
        {
            return;
        }

        var path = files[0].TryGetLocalPath();
        if (path is null)
        {
            Status.Text = "Choose a local CSV file.";
            return;
        }
        await Run(async () =>
        {
            var preview = await Task.Run(() => _store.PreviewCsvImport(path));
            var summary = preview.Summary;
            var message = $"{summary.Rows} rows: {summary.Expenses} expenses, {summary.Incomes} income, {summary.Transfers} paired transfers, {summary.Openings} openings.\n\n" +
                "Resolved dates (first 10 rows, 2000–2099): " +
                string.Join(", ", preview.ResolvedDates.Select(d => $"line {d.Line}: {d.Date:yyyy-MM-dd}")) +
                "\n\nAccounts and source totals:\n" +
                string.Join("\n", summary.AccountTotals.Select(a => $"{a.Account}: {a.Centimes / 100m:N2}")) +
                "\n\nCategories: " + string.Join(", ", summary.Categories) +
                (preview.Issues.Count == 0 ? "\n\nAll rows are valid. Apply this batch?" :
                    "\n\nErrors (correct source file, then preview again):\n" +
                    string.Join("\n", preview.Issues.Take(30).Select(i => $"Line {i.Line}: {i.Message}")));
            var dialog = new Window
            {
                Title = "CSV import preview",
                Width = 700,
                Height = 650,
                MinWidth = 500,
                MinHeight = 350,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            var body = new StackPanel
            {
                Spacing = 12,
                Margin = new Thickness(20)
            };
            body.Children.Add(Text(message));
            var apply = new Button
            {
                Content = "Apply import",
                IsEnabled = preview.CanApply
            };
            var cancel = new Button
            {
                Content = "Cancel",
                IsCancel = true
            };
            var error = Text("");
            error.Foreground = Brushes.DarkRed;
            body.Children.Add(error);
            body.Children.Add(Row(apply, cancel));
            dialog.Content = new ScrollViewer { Content = body };
            cancel.Click += (_, _) => dialog.Close();
            apply.Click += async (_, _) =>
            {
                apply.IsEnabled = false;
                try
                {
                    var result = await Task.Run(() => _store.ApplyCsvImport(preview));
                    dialog.Close();
                    _historyCursors.Clear();
                    _historyOffset = 0;
                    await Refresh();
                    Status.Text = $"Imported {result.Added} entries; {result.Unchanged} unchanged.";
                }
                catch (Exception ex) { error.Text = FriendlyError(ex); apply.IsEnabled = true; }
            };
            await dialog.ShowDialog(this);
        });
    }

    private async Task ExportCsv()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export CSV",
            SuggestedFileName = $"balancia-{DateTime.Today:yyyyMMdd}.csv",
            FileTypeChoices = [new FilePickerFileType("CSV") { Patterns = ["*.csv"] }]

        });
        var path = file?.TryGetLocalPath();
        if (path is null)
        {
            return;
        }

        await Run(async () =>
        {
            var count = await Task.Run(() => _store.ExportCsv(path));
            Status.Text = $"Exported {count} rows to CSV.";
        });
    }

    private async Task ExportSnapshot()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Balancia snapshot",
            SuggestedFileName = $"balancia-{DateTime.Today:yyyyMMdd}.balancia",
            FileTypeChoices = [new FilePickerFileType("Balancia snapshot") { Patterns = ["*.balancia"] }]

        });
        var path = file?.TryGetLocalPath();
        if (path is null)
        {
            return;
        }

        await Run(async () => { var manifest = await Task.Run(() => _store.ExportSnapshot(path)); Status.Text = $"Snapshot exported · revision {manifest.Revision}"; });
    }

    private async Task RestoreSnapshot()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose Balancia snapshot",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Balancia snapshot") { Patterns = ["*.balancia"] }]

        });
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (path is null)
        {
            return;
        }

        await Run(async () =>
        {
            var manifest = await Task.Run(() => _store.ValidateSnapshot(path));
            var backup = await Task.Run(() => _store.RestoreSnapshot(path));
            Status.Text = $"Snapshot restored · revision {manifest.Revision} · backup {Path.GetFileName(backup)}";
            await Refresh();
        });
    }
    private async Task EditAccount(Account? account)
    {
        var name = Input(account?.Name ?? "");
        var date = Input((account?.OpeningDate ?? _displayDate).ToString("yyyy-MM-dd"));
        var amount = Input((account?.OpeningAmount.Francs ?? 0).ToString("0.00", CultureInfo.InvariantCulture));
        var archived = new CheckBox
        {
            Content = "Archived",
            IsChecked = account?.Archived ?? false
        };
        await EditDialog(account is null ? "Add account" : "Edit account", [Field("Name", name), Field("Opening date (YYYY-MM-DD)", date), Field("Opening amount", amount), archived],
            () => { var values = (name.Text ?? "", ParseDate(date), ParseMoney(amount), archived.IsChecked == true); return () => _store.SaveAccount(account?.Id, values.Item1, values.Item2, values.Item3, values.Item4); });
    }

    private async Task EditCategory(Category? category)
    {
        var name = Input(category?.Name ?? "");
        var options = new List<Choice<string?>> { new(null, "No parent (top-level)") };
        options.AddRange(_snapshot!.Categories.Where(c => c.ParentId is null && !c.Archived && c.Id != category?.Id).Select(c => new Choice<string?>(c.Id, c.Path)));
        if (category?.ParentId is { } current && options.All(c => c.Value != current))
        {
            options.Add(new(current, _snapshot.Categories.Single(c => c.Id == current).ToString()));
        }

        var parent = new ComboBox
        {
            ItemsSource = options,
            SelectedItem = options.FirstOrDefault(c => c.Value == category?.ParentId) ?? options[0],
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var archived = new CheckBox
        {
            Content = "Archived",
            IsChecked = category?.Archived ?? false
        };
        await EditDialog(category is null ? "Add category" : "Edit category", [Field("Name", name), Field("Parent category", parent), archived],
            () => { var values = (name.Text ?? "", ((Choice<string?>)parent.SelectedItem!).Value, archived.IsChecked == true); return () => _store.SaveCategory(category?.Id, values.Item1, values.Item2, values.Item3); });
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

    // Snapshot inputs on the UI thread, then perform the complete write off-thread.
    private async Task EditDialog(string title, Control[] fields, Func<Action> prepareSave, string saveLabel = "Save")
    {
        var dialog = new Window
        {
            Title = title,
            Width = 530,
            Height = title.Contains("transaction", StringComparison.OrdinalIgnoreCase) ? 730 : 480,
            MinWidth = 430,
            MinHeight = 360,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brushes.White
        };
        var body = new StackPanel
        {
            Spacing = 12,
            Margin = new Thickness(24)
        };
        body.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 24,
            FontWeight = FontWeight.SemiBold
        });
        foreach (var field in fields)
        {
            body.Children.Add(field);
        }

        var error = Text("");
        error.Foreground = Brushes.DarkRed;
        body.Children.Add(error);
        var save = new Button
        {
            Content = saveLabel,
            IsDefault = saveLabel == "Save"
        };
        var cancel = new Button
        {
            Content = "Cancel",
            IsCancel = true
        };
        body.Children.Add(Row(save, cancel));
        dialog.Content = new ScrollViewer { Content = body };
        var saving = false;
        cancel.Click += (_, _) => dialog.Close();
        dialog.Closing += (_, e) =>
        {
            if (saving)
            {
                e.Cancel = true;
            }
        };
        dialog.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && !saving)
            {
                dialog.Close();
            }
        };
        save.Click += async (_, _) =>
        {
            if (saving)
            {
                return;
            }

            try
            {
                var action = prepareSave();
                saving = true;
                body.IsEnabled = false;
                error.Text = "Saving…";
                await Task.Run(action);
                saving = false;
                _historyCursors.Clear();
                _historyOffset = 0;
                dialog.Close();
            }
            catch (Exception ex) { error.Text = FriendlyError(ex); }
            finally { saving = false; body.IsEnabled = true; }
        };
        dialog.Opened += (_, _) =>
        {
            if (fields.FirstOrDefault() is StackPanel panel && panel.Children.LastOrDefault() is InputElement input)
            {
                input.Focus();
            }
        };
        await dialog.ShowDialog(this);
        await Run(Refresh);
    }

    private static Money ParseMoney(TextBox input) => Money.FromFrancs(decimal.Parse(input.Text ?? "", NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite, CultureInfo.InvariantCulture));
    private static Money? OptionalMoney(TextBox input) => string.IsNullOrWhiteSpace(input.Text) ? null : ParseMoney(input);
    private static DatePicker DateInput(DateOnly? date) => new()
    {
        SelectedDate = date?.ToDateTime(TimeOnly.MinValue),
        HorizontalAlignment = HorizontalAlignment.Stretch
    };
    private static DateOnly? OptionalDate(TextBox input) => string.IsNullOrWhiteSpace(input.Text) ? null : ParseDate(input);
    private static DateOnly ParseDate(TextBox input) => DateOnly.ParseExact(input.Text ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static TextBox Input(string text) => new()
    {
        Text = text,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };
    private static TextBlock Text(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        Foreground = Brush.Parse("#344D44")
    };
    private static string AmountText(Money value) => value.Francs.ToString("N2", CultureInfo.GetCultureInfo("de-CH"));
    private static string EntryText(LedgerEntry entry) => $"{entry.Draft.Date:yyyy-MM-dd}  ·  {entry.Draft.Kind}  ·  {AmountText(entry.Draft.Amount)}  ·  {entry.AccountName}{(entry.DestinationName is null ? "" : " → " + entry.DestinationName)}  ·  {entry.Draft.Description}  ·  {entry.CategoryPath ?? ""}";
    private static StackPanel Field(string label, Control input)
    {
        AutomationProperties.SetName(input, label);
        return new()
        {
            Spacing = 5,
            Children = { Text(label), input }
        };
    }
    private static WrapPanel Row(params Control[] controls)
    {
        var row = new WrapPanel();
        foreach (var control in controls)
        {
            control.Margin = new Thickness(0, 0, 10, 10);
            row.Children.Add(control);
        }
        return row;
    }
    private static Border Card(string heading, string message) => new()
    {
        Background = Brushes.White,
        CornerRadius = new CornerRadius(10),
        Padding = new Thickness(20),
        Child = new StackPanel
        {
            Spacing = 10,
            Children = { new TextBlock {
Text = heading, FontSize = 18, FontWeight = FontWeight.SemiBold
}, Text(message) }
        }
    };
    private static Button ActionButton(string title, Func<Task> action)
    {
        var button = new Button { Content = title };
        button.Click += async (_, _) => await action();
        return button;
    }
    private sealed record Choice<T>(T Value, string Label)
    {
        public override string ToString() => Label;
    }
    private sealed record HistoryItem(HistoryHit Hit)
    {
        public override string ToString()
        {
            var entry = Hit.Entry;
            return $"{entry.Draft.Date:yyyy-MM-dd} · {entry.Draft.Description} · {entry.Draft.Kind} · {AmountText(entry.Draft.Amount)} · {entry.AccountName}";
        }
    }
}
