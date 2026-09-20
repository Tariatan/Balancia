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
}
