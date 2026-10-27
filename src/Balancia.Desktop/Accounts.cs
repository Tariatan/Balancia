using System.Globalization;
using Avalonia.Controls;
using Balancia.Core;
using static Balancia.Desktop.FormControls;
using static Balancia.Desktop.InputParsing;
using static Balancia.Desktop.Localization.UiText;

namespace Balancia.Desktop;

public partial class MainWindow
{
    private void InitializeAccountPanel()
    {
        AccountsCard.AddRequested += async () => await EditAccount(null);
        AccountsCard.EditRequested += async account => await EditAccount(account);
        AccountsCard.ArchiveRequested += async () => await ArchiveSelectedAccount();
        AccountsCard.RemoveRequested += async () => await DeleteSelectedAccount();
        AccountsCard.FilterToggled += async (id, selected) => await ToggleAccountFilter(id, selected);
    }

    private void RenderAccountPanel(LedgerSnapshot ledgerSnapshot) =>
        AccountsCard.Render(ledgerSnapshot.Accounts, ledgerSnapshot.NetWorth, settings.DefaultAccountId, overviewState.IsAccountSelected);

    private async Task ToggleAccountFilter(string id, bool selected)
    {
        if (updatingFilterControls)
        {
            return;
        }

        overviewState.ToggleAccount(id, selected);
        AccountsCard.SyncFilterChecks();
        await RequestOverviewFilterRefresh();
    }

    private async Task ArchiveSelectedAccount()
    {
        if (AccountsCard.Selected is not { } account)
        {
            return;
        }

        await EditDialog("Archive account",
            [
                Text(Format("Archive '{0}'?", account.Name))
            ],
            () => () => store.SaveAccount(account.Id, account.Name, account.OpeningDate, account.OpeningAmount, true),
            DialogOptions.Confirmation("Archive"));
    }

    private async Task DeleteSelectedAccount()
    {
        if (AccountsCard.Selected is not { } account)
        {
            await SelectFirst();
            return;
        }

        await EditDialog("Delete account",
            [
                Text(Format("Delete '{0}'?", account.Name))
            ],
            () => () => store.DeleteAccount(account.Id), DialogOptions.Confirmation("Delete"));
    }

    private async Task EditAccount(Account? account)
    {
        var name = Input(account?.Name ?? "");
        var date = DateInput(account?.OpeningDate ?? displayDate);
        var amount = Input((account?.OpeningAmount.Francs ?? 0).ToString("0.00", CultureInfo.InvariantCulture));
        var defaultAccount = new CheckBox
        {
            Content = Get("Use as default account for new transactions"),
            IsChecked = account is not null && account.Id == settings.DefaultAccountId
        };

        await EditDialog(account is null ? "Add account" : "Edit account",
            [
                Field("Name", name),
                Field("Opening date", date),
                Field("Opening amount", amount), defaultAccount
            ],
            () =>
            {
                var accountName = name.Text ?? "";
                var openingDate = ParseDate(date);
                var openingAmount = ParseMoney(amount);
                var archived = account?.Archived ?? false;
                var useAsDefault = defaultAccount.IsChecked == true;

                return () =>
                {
                    var savedId = store.SaveAccount(account?.Id, accountName, openingDate, openingAmount, archived);
                    if (useAsDefault && savedId != settings.DefaultAccountId)
                    {
                        // Persist first so a failed settings write leaves the in-memory default unchanged.
                        var updated = settings with { DefaultAccountId = savedId };
                        PersistSettings(updated);
                        settings = updated;
                    }
                };
            },
            new DialogOptions(Validate: () =>
            {
                if (string.IsNullOrWhiteSpace(name.Text))
                {
                    return "Enter an account name.";
                }

                return ParseDate(date) > Today ? "Opening date cannot be in the future." : null;
            }));
    }
}
