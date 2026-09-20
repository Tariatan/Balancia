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
    private async Task ArchiveSelectedAccount(ListBox accounts)
    {
        if (accounts.SelectedItem is not Choice<Account> choice)
        {
            return;
        }

        await EditDialog("Archive account",
            [Text($"Archive '{choice.Value.Name}'?")],
            () => () => _store.SaveAccount(choice.Value.Id, choice.Value.Name, choice.Value.OpeningDate, choice.Value.OpeningAmount, true), "Archive");
    }

    private async Task DeleteSelectedAccount(ListBox accounts)
    {
        if (accounts.SelectedItem is not Choice<Account> choice)
        {
            await SelectFirst();
            return;
        }

        var dialog = new Window
        {
            Title = "Delete account",
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
                await Task.Run(() => _store.DeleteAccount(choice.Value.Id));
                dialog.Close();
                await Run(Refresh);
            }
            catch (Exception ex)
            {
                await ShowErrorDialog("Balancia", FriendlyError(ex));
                remove.IsEnabled = true;
            }
        };
        cancel.Click += (_, _) => dialog.Close();
        dialog.Content = new StackPanel
        {
            Spacing = 14,
            Margin = new Thickness(22),
            Children =
            {
                Text($"Delete '{choice.Value.Name}'?"),
                Row(remove, cancel)
            }
        };
        await dialog.ShowDialog(this);
    }

    private async Task EditAccount(Account? account)
    {
        var name = Input(account?.Name ?? "");
        var date = Input((account?.OpeningDate ?? _displayDate).ToString("yyyy-MM-dd"));
        var amount = Input((account?.OpeningAmount.Francs ?? 0).ToString("0.00", CultureInfo.InvariantCulture));
        await EditDialog(account is null ? "Add account" : "Edit account",
            [Field("Name", name), Field("Opening date (YYYY-MM-DD)", date), Field("Opening amount", amount)],
            () =>
            {
                var values = (name.Text ?? "", ParseDate(date), ParseMoney(amount), account?.Archived ?? false);
                return () => _store.SaveAccount(account?.Id, values.Item1, values.Item2, values.Item3, values.Item4);
            });
    }
}
