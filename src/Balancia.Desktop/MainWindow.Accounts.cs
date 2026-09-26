using System.Globalization;
using Avalonia.Controls;
using Balancia.Core;

namespace Balancia.Desktop;

public partial class MainWindow
{
    private readonly Dictionary<string, CheckBox> accountFilterChecks = [];

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
                Text($"Archive '{choice.Value.Name}'?")
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
                Text($"Delete '{choice.Value.Name}'?")
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
            Content = "Use as default account for new transactions",
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
