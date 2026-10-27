using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Balancia.Core;

using static Balancia.Desktop.InputParsing;
using static Balancia.Desktop.Localization.UiText;

namespace Balancia.Desktop;

/// <summary>
/// The accounts card: balances with a checkbox per account that filters the overview, and the total net worth. It draws what it is given
/// and raises requests; dialogs, saving and the filter itself stay with the window.
/// </summary>
public partial class AccountsCard : UserControl
{
    private readonly Dictionary<string, CheckBox> filterChecks = [];
    private Func<string, bool> isFiltered = _ => false;
    private string? defaultAccountId;
    private bool syncing;

    public AccountsCard()
    {
        InitializeComponent();
        AccountsList.ItemTemplate = new FuncDataTemplate<Account>((account, _) =>
        {
            // Virtualized presenters can request a template with no item while scrolling.
            if (account is null)
            {
                return null;
            }

            var check = new CheckBox
            {
                Classes = { "compact-filter-checkbox" },
                IsChecked = isFiltered(account.Id),
            };
            AutomationProperties.SetName(check, account.Name);
            filterChecks[account.Id] = check;
            check.IsCheckedChanged += (_, _) =>
            {
                if (syncing)
                {
                    return;
                }

                if (check.FindAncestorOfType<ListBox>() is { } list)
                {
                    list.SelectedItem = account;
                }

                FilterToggled?.Invoke(account.Id, check.IsChecked == true);
            };
            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                ColumnSpacing = 5,
                MinHeight = 18,
                Margin = new Thickness(0, 5),
            };
            if (account.Id == defaultAccountId)
            {
                var highlight = new Border
                {
                    Background = Brush.Parse("#403989A7"),
                    Margin = new Thickness(-4, 0),
                    IsHitTestVisible = false,
                };
                Grid.SetColumnSpan(highlight, 3);
                row.Children.Add(highlight);
            }

            row.Children.Add(check);
            GridLayout.Place(row, new TextBlock
            {
                Text = account.Name,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            }, 1);
            GridLayout.Place(row, new TextBlock
            {
                Text = AmountText(account.Balance),
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                Foreground = BalanceBrushes.For(account.Balance),
                VerticalAlignment = VerticalAlignment.Center
            }, 2);
            return row;
        });
        AccountsList.DoubleTapped += (_, _) =>
        {
            if (Selected is { } account)
            {
                EditRequested?.Invoke(account);
            }
        };
        AccountAddButton.Click += (_, _) => AddRequested?.Invoke();
        AccountArchiveButton.Click += (_, _) => ArchiveRequested?.Invoke();
        AccountRemoveButton.Click += (_, _) => RemoveRequested?.Invoke();
    }

    /// <summary>The add button was pressed.</summary>
    public event Action? AddRequested;

    /// <summary>The archive button was pressed; <see cref="Selected"/> says what, if anything, it refers to.</summary>
    public event Action? ArchiveRequested;

    /// <summary>The remove button was pressed; <see cref="Selected"/> says what, if anything, it refers to.</summary>
    public event Action? RemoveRequested;

    /// <summary>A row was double-tapped.</summary>
    public event Action<Account>? EditRequested;

    /// <summary>A row's checkbox was changed by the user: the account id and whether it is now selected.</summary>
    public event Action<string, bool>? FilterToggled;

    public Account? Selected => AccountsList.SelectedItem as Account;

    /// <summary>Shows the accounts and the net worth with the current language. <paramref name="isFiltered"/> says which accounts the overview is filtered to.</summary>
    public void Render(IReadOnlyList<Account> accounts, Money netWorth, string? defaultAccountId, Func<string, bool> isFiltered)
    {
        this.defaultAccountId = defaultAccountId;
        this.isFiltered = isFiltered;
        AccountsHeading.Text = Get("Accounts");
        ToolTip.SetTip(AccountAddButton, Get("Add account"));
        ToolTip.SetTip(AccountArchiveButton, Get("Archive selected account"));
        ToolTip.SetTip(AccountRemoveButton, Get("Delete selected account"));

        filterChecks.Clear();
        AccountsList.ItemsSource = accounts.ToArray();
        var hasAccounts = accounts.Count > 0;
        AccountsList.IsVisible = hasAccounts;
        AccountsEmpty.IsVisible = !hasAccounts;
        AccountsEmpty.Text = Get("No accounts yet");

        NetWorthLabel.Text = Get("Total net worth");
        NetWorthValue.Text = AmountText(netWorth);
        NetWorthValue.Foreground = BalanceBrushes.For(netWorth);
    }

    /// <summary>Brings the checkboxes in line with the filter, without raising <see cref="FilterToggled"/>.</summary>
    public void SyncFilterChecks()
    {
        var wasSyncing = syncing;
        syncing = true;
        try
        {
            foreach (var (id, check) in filterChecks)
            {
                check.IsChecked = isFiltered(id);
            }
        }
        finally
        {
            syncing = wasSyncing;
        }
    }
}
