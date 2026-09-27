using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Balancia.Core;
using Balancia.Storage;

using static Balancia.Desktop.Localization.UiText;

namespace Balancia.Desktop;

public partial class MainWindow
{
    private readonly Dictionary<string, CheckBox> accountFilterChecks = [];

    private Border AccountSummaryPanel(LedgerSnapshot ledgerSnapshot)
    {
        var accountRows = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            RowSpacing = 5
        };
        var accountHeader = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(0, 0, 0, 4)
        };
        accountHeader.Children.Add(Heading("Accounts", 11));
        var accountActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 3
        };
        accountFilterChecks.Clear();
        var accounts = new ListBox
        {
            Classes = { "compact-list" },
            ItemsSource = ledgerSnapshot.Accounts.Select(a => new Choice<Account>(a, a.Name)).ToArray(),
            MinHeight = ledgerSnapshot.Accounts.Count == 0 ? 0 : 45,
            MaxHeight = 170,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            ItemTemplate = new FuncDataTemplate<Choice<Account>>((choice, _) =>
            {
                if (choice is null)
                {
                    return null;
                }

                var check = new CheckBox
                {
                    Classes = { "compact-filter-checkbox" },
                    IsChecked = overviewFilter.AccountId == choice.Value.Id || overviewFilter.AccountIds?.Contains(choice.Value.Id) == true,
                };
                AutomationProperties.SetName(check, choice.Value.Name);
                accountFilterChecks[choice.Value.Id] = check;
                check.IsCheckedChanged += async (_, _) =>
                {
                    if (!updatingFilterControls && check.FindAncestorOfType<ListBox>() is { } list)
                    {
                        list.SelectedItem = choice;
                    }

                    await ToggleAccountFilter(choice.Value.Id, check.IsChecked == true);
                };
                var row = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                    ColumnSpacing = 5,
                    MinHeight = 18,
                    Margin = new Thickness(0, 5)
                };
                row.Children.Add(check);
                AddColumn(row, new TextBlock
                {
                    Text = choice.Value.Name,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                }, 1);
                AddColumn(row, new TextBlock
                {
                    Text = AmountText(choice.Value.Balance),
                    FontSize = 12,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = BalanceColor(choice.Value.Balance),
                    VerticalAlignment = VerticalAlignment.Center
                }, 2);
                return row;
            })
        };
        accounts.DoubleTapped += async (_, _) =>
        {
            if (accounts.SelectedItem is Choice<Account> selected)
            {
                await EditAccount(selected.Value);
            }
        };
        var addAccount = ActionButton("+", () => EditAccount(null));
        addAccount.Width = 30;
        addAccount.Padding = new Thickness(0);
        addAccount.FontSize = 18;
        addAccount.HorizontalContentAlignment = HorizontalAlignment.Center;

        var removeAccount = ActionButton("🗑", () => DeleteSelectedAccount(accounts));
        removeAccount.Width = 30;
        removeAccount.Padding = new Thickness(0);
        removeAccount.FontSize = 18;
        removeAccount.HorizontalContentAlignment = HorizontalAlignment.Center;

        var archiveAccount = ActionButton("▣", () => ArchiveSelectedAccount(accounts));
        archiveAccount.Width = 30;
        archiveAccount.Padding = new Thickness(0);
        archiveAccount.FontSize = 18;
        archiveAccount.HorizontalContentAlignment = HorizontalAlignment.Center;

        ToolTip.SetTip(addAccount, Get("Add account"));
        ToolTip.SetTip(archiveAccount, Get("Archive selected account"));
        ToolTip.SetTip(removeAccount, Get("Delete selected account"));
        accountActions.Children.Add(addAccount);
        accountActions.Children.Add(archiveAccount);
        accountActions.Children.Add(removeAccount);
        AddColumn(accountHeader, accountActions, 1);
        AddRow(accountRows, accountHeader, 0);

        if (ledgerSnapshot.Accounts.Count == 0)
        {
            var emptyAccounts = QuietText("No accounts yet", 12);
            emptyAccounts.VerticalAlignment = VerticalAlignment.Center;
            AddRow(accountRows, emptyAccounts, 1);
        }
        else
        {
            AddRow(accountRows, accounts, 1);
        }

        var total = TwoColumn("Total net worth", AmountText(ledgerSnapshot.NetWorth), 16,
            BalanceColor(ledgerSnapshot.NetWorth));
        total.Margin = new Thickness(0, 10, 0, 0);
        AddRow(accountRows, total, 2);
        var accountPanel = Panel(accountRows);
        accountPanel.Padding = new Thickness(10, 12, 10, 8);
        return accountPanel;
    }

    private void SyncAccountFilterChecks()
    {
        var wasUpdating = updatingFilterControls;
        updatingFilterControls = true;
        try
        {
            foreach (var (id, check) in accountFilterChecks)
            {
                check.IsChecked = overviewFilter.AccountId == id || overviewFilter.AccountIds?.Contains(id) == true;
            }
        }
        finally
        {
            updatingFilterControls = wasUpdating;
        }
    }

    private async Task ToggleAccountFilter(string id, bool selected)
    {
        if (updatingFilterControls)
        {
            return;
        }

        var ids = new HashSet<string>(overviewFilter.AccountIds ?? []);
        if (overviewFilter.AccountId is { } single)
        {
            ids.Add(single);
        }

        if (selected)
        {
            ids.Add(id);
        }
        else
        {
            ids.Remove(id);
        }

        overviewFilter = overviewFilter with
        {
            AccountId = ids.Count == 1 ? ids.Single() : null,
            AccountIds = ids.Count > 1 ? ids.Order().ToArray() : null,
        };
        overviewOffset = 0;
        SyncAccountFilterChecks();
        await RequestOverviewFilterRefresh();
    }

    private async Task ArchiveSelectedAccount(ListBox accounts)
    {
        if (accounts.SelectedItem is not Choice<Account> choice)
        {
            return;
        }

        await EditDialog("Archive account",
            [
                Text(Format("Archive '{0}'?", choice.Value.Name))
            ],
            () => () => store.SaveAccount(choice.Value.Id, choice.Value.Name, choice.Value.OpeningDate, choice.Value.OpeningAmount, true),
            "Archive");
    }

    private async Task DeleteSelectedAccount(ListBox accounts)
    {
        if (accounts.SelectedItem is not Choice<Account> choice)
        {
            await SelectFirst();
            return;
        }

        await EditDialog("Delete account",
            [
                Text(Format("Delete '{0}'?", choice.Value.Name))
            ],
            () => () => store.DeleteAccount(choice.Value.Id), "Delete");
    }

    private async Task EditAccount(Account? account)
    {
        var name = Input(account?.Name ?? "");
        var date = DateInput(account?.OpeningDate ?? displayDate);
        var amount = Input((account?.OpeningAmount.Francs ?? 0).ToString("0.00", CultureInfo.InvariantCulture));
        var defaultAccount = new CheckBox
        {
            Content = Get("Use as default account for new transactions"),
            IsChecked = account is not null && account.Id == defaultAccountId
        };

        await EditDialog(account is null ? "Add account" : "Edit account",
            [
                Field("Name", name),
                Field("Opening date", date),
                Field("Opening amount", amount), defaultAccount
            ],
            () =>
            {
                var values = (name.Text ?? "", ParseDate(date), ParseMoney(amount), account?.Archived ?? false);
                var useAsDefault = defaultAccount.IsChecked == true;
                if (useAsDefault && account?.Id is not null)
                {
                    defaultAccountId = account.Id;
                }

                return () =>
                {
                    store.SaveAccount(account?.Id, values.Item1, values.Item2, values.Item3, values.Item4);
                    if (useAsDefault && account?.Id is not null)
                    {
                        SaveApplicationSettings(databasePath, backupPath, snapshotPath, defaultAccountId);
                    }
                };
            });
    }
}
