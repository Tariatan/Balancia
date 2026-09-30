using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Balancia.Core;
using static Balancia.Desktop.Localization.UiText;

namespace Balancia.Desktop;

public partial class MainWindow
{
    private readonly Dictionary<string, CheckBox> accountFilterChecks = [];

    private void InitializeAccountPanel()
    {
        AccountsList.ItemTemplate = new FuncDataTemplate<Choice<Account>>((choice, _) =>
        {
            // Virtualized presenters can request a template with no item while scrolling.
            if (choice is null)
            {
                return null;
            }

            var check = new CheckBox
            {
                Classes = { "compact-filter-checkbox" },
                IsChecked = filter.AccountId == choice.Value.Id || filter.AccountIds?.Contains(choice.Value.Id) == true,
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
        });
        AccountsList.DoubleTapped += async (_, _) =>
        {
            if (AccountsList.SelectedItem is Choice<Account> selected)
            {
                await EditAccount(selected.Value);
            }
        };
        AccountAddButton.Click += async (_, _) => await EditAccount(null);
        AccountArchiveButton.Click += async (_, _) => await ArchiveSelectedAccount(AccountsList);
        AccountRemoveButton.Click += async (_, _) => await DeleteSelectedAccount(AccountsList);
    }

    private void RenderAccountPanel(LedgerSnapshot ledgerSnapshot)
    {
        AccountsHeading.Text = Get("Accounts");
        ToolTip.SetTip(AccountAddButton, Get("Add account"));
        ToolTip.SetTip(AccountArchiveButton, Get("Archive selected account"));
        ToolTip.SetTip(AccountRemoveButton, Get("Delete selected account"));

        accountFilterChecks.Clear();
        AccountsList.ItemsSource = ledgerSnapshot.Accounts.Select(a => new Choice<Account>(a, a.Name)).ToArray();
        var hasAccounts = ledgerSnapshot.Accounts.Count > 0;
        AccountsList.IsVisible = hasAccounts;
        AccountsEmpty.IsVisible = !hasAccounts;
        AccountsEmpty.Text = Get("No accounts yet");

        NetWorthLabel.Text = Get("Total net worth");
        NetWorthValue.Text = AmountText(ledgerSnapshot.NetWorth);
        NetWorthValue.Foreground = BalanceColor(ledgerSnapshot.NetWorth);
    }

    private void SyncAccountFilterChecks()
    {
        var wasUpdating = updatingFilterControls;
        updatingFilterControls = true;
        try
        {
            foreach (var (id, check) in accountFilterChecks)
            {
                check.IsChecked = filter.AccountId == id || filter.AccountIds?.Contains(id) == true;
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

        var ids = new HashSet<string>(filter.AccountIds ?? []);
        if (filter.AccountId is { } single)
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

        filter = filter with
        {
            AccountId = ids.Count == 1 ? ids.Single() : null,
            AccountIds = ids.Count > 1 ? ids.Order().ToArray() : null,
        };
        offset = 0;
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
            },
            validate: () =>
            {
                if (string.IsNullOrWhiteSpace(name.Text))
                {
                    return "Enter an account name.";
                }

                if (ParseDate(date) > DateOnly.FromDateTime(DateTime.Today))
                {
                    return "Opening date cannot be in the future.";
                }

                return null;
            });
    }
}
