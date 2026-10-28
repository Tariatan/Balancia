using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Balancia.Core;
using Xunit;

namespace Balancia.Desktop.Tests;

public sealed class AccountsCardTests
{
    private static Account Account(string id, string name, decimal balance = 100m) =>
        new(id, name, new DateOnly(2026, 1, 1), Money.Zero, false, Money.FromFrancs(balance));

    private static (Window Window, AccountsCard Card) Show()
    {
        var card = new AccountsCard();
        var window = new Window { Width = 420, Height = 360, Content = card };
        window.Show();
        return (window, card);
    }

    private static T Find<T>(AccountsCard card, string name) where T : Control => card.FindControl<T>(name)!;

    private static CheckBox[] Checks(AccountsCard card) =>
        [.. Find<ListBox>(card, "AccountsList").GetLogicalDescendants().OfType<CheckBox>()];

    private static CheckBox CheckFor(AccountsCard card, string accountName) =>
        Checks(card).Single(check => AutomationProperties.GetName(check) == accountName);

    private static void Render(AccountsCard card, Window window, IReadOnlyList<Account> accounts, Func<string, bool>? isFiltered = null,
        string? defaultAccountId = null, decimal netWorth = 200m)
    {
        card.Render(accounts, Money.FromFrancs(netWorth), defaultAccountId, isFiltered ?? (_ => false));
        window.UpdateLayout();
    }

    [AvaloniaFact]
    public void Render_WithAccounts_ListsThemAndShowsTheNetWorth()
    {
        // Arrange
        var (window, card) = Show();

        // Act
        Render(card, window, [Account("a", "Everyday", 150m), Account("b", "Savings", 50m)], netWorth: 200m);

        // Assert
        Assert.Equal(2, Find<ListBox>(card, "AccountsList").ItemCount);
        Assert.True(Find<ListBox>(card, "AccountsList").IsVisible);
        Assert.False(Find<TextBlock>(card, "AccountsEmpty").IsVisible);
        Assert.Equal(InputParsing.AmountText(Money.FromFrancs(200m)), Find<TextBlock>(card, "NetWorthValue").Text);
    }

    [AvaloniaFact]
    public void Render_WithoutAccounts_ShowsTheEmptyMessageAndHidesTheList()
    {
        // Arrange
        var (window, card) = Show();

        // Act
        Render(card, window, [], netWorth: 0m);

        // Assert
        Assert.False(Find<ListBox>(card, "AccountsList").IsVisible);
        Assert.True(Find<TextBlock>(card, "AccountsEmpty").IsVisible);
    }

    [AvaloniaTheory]
    [InlineData(250, true)]
    [InlineData(0, false)] // zero is not positive
    [InlineData(-30, false)]
    public void Render_NetWorth_IsGreenOnlyWhenPositive(int netWorth, bool positive)
    {
        // Arrange
        var (window, card) = Show();

        // Act
        Render(card, window, [Account("a", "Everyday")], netWorth: netWorth);

        // Assert
        Assert.Same(positive ? Palette.Positive : Palette.Negative, Find<TextBlock>(card, "NetWorthValue").Foreground);
    }

    [AvaloniaFact]
    public void Row_AccessibilityName_IsTheAccountName()
    {
        // Arrange
        var (window, card) = Show();
        Render(card, window, [Account("a", "Everyday", 150m)]);
        var row = (ListBoxItem)Find<ListBox>(card, "AccountsList").ContainerFromIndex(0)!;

        // Act
        var name = ControlAutomationPeer.CreatePeerForElement(row).GetName();

        // Assert
        Assert.Equal("Everyday", name);
    }

    [AvaloniaFact]
    public void Render_Again_ReplacesTheRowsAndTheirCheckboxes()
    {
        // Arrange
        var (window, card) = Show();
        Render(card, window, [Account("a", "Everyday"), Account("b", "Savings")]);

        // Act
        Render(card, window, [Account("c", "Cash")]);

        // Assert
        Assert.Equal(1, Find<ListBox>(card, "AccountsList").ItemCount);
        Assert.Equal(["Cash"], Checks(card).Select(AutomationProperties.GetName));
    }

    [AvaloniaFact]
    public void Row_OfTheDefaultAccount_HasTheHighlightAndNoOtherRowDoes()
    {
        // Arrange
        var (window, card) = Show();

        // Act
        Render(card, window, [Account("a", "Everyday"), Account("b", "Savings")], defaultAccountId: "b");

        // Assert
        bool Highlighted(string name) => CheckFor(card, name).GetLogicalParent<Grid>()!.Children.OfType<Border>().Any();
        Assert.False(Highlighted("Everyday"));
        Assert.True(Highlighted("Savings"));
    }

    [AvaloniaFact]
    public void Checkboxes_FollowTheFilterPredicate()
    {
        // Arrange
        var (window, card) = Show();

        // Act
        Render(card, window, [Account("a", "Everyday"), Account("b", "Savings")], isFiltered: id => id == "b");

        // Assert
        Assert.False(CheckFor(card, "Everyday").IsChecked);
        Assert.True(CheckFor(card, "Savings").IsChecked);
    }

    [AvaloniaFact]
    public void SyncFilterChecks_AfterTheFilterChanged_UpdatesTheBoxesWithoutRaisingToggles()
    {
        // Arrange
        var (window, card) = Show();
        var selected = new HashSet<string> { "b" };
        Render(card, window, [Account("a", "Everyday"), Account("b", "Savings")], isFiltered: selected.Contains);
        var toggles = new List<(string Id, bool Selected)>();
        card.FilterToggled += (id, isSelected) => toggles.Add((id, isSelected));
        selected.Clear();
        selected.Add("a");

        // Act
        card.SyncFilterChecks();

        // Assert
        Assert.True(CheckFor(card, "Everyday").IsChecked);
        Assert.False(CheckFor(card, "Savings").IsChecked);
        Assert.Empty(toggles);
    }

    [AvaloniaFact]
    public void Checkbox_ChangedByTheUser_RaisesFilterToggledAndSelectsTheRow()
    {
        // Arrange
        var (window, card) = Show();
        var accounts = new[] { Account("a", "Everyday"), Account("b", "Savings") };
        Render(card, window, accounts);
        var toggles = new List<(string Id, bool Selected)>();
        card.FilterToggled += (id, isSelected) => toggles.Add((id, isSelected));

        // Act
        CheckFor(card, "Savings").IsChecked = true;

        // Assert
        Assert.Equal([("b", true)], toggles);
        Assert.Same(accounts[1], card.Selected);
    }

    [AvaloniaFact]
    public void Buttons_Pressed_RaiseTheirRequests()
    {
        // Arrange
        var (_, card) = Show();
        var raised = new List<string>();
        card.AddRequested += () => raised.Add("add");
        card.ArchiveRequested += () => raised.Add("archive");
        card.RemoveRequested += () => raised.Add("remove");

        // Act
        Find<Button>(card, "AccountAddButton").Press();
        Find<Button>(card, "AccountArchiveButton").Press();
        Find<Button>(card, "AccountRemoveButton").Press();

        // Assert
        Assert.Equal(["add", "archive", "remove"], raised);
    }

    [AvaloniaFact]
    public void Selected_NothingChosen_IsNullAndTheChosenRowOtherwise()
    {
        // Arrange
        var (window, card) = Show();
        var accounts = new[] { Account("a", "Everyday"), Account("b", "Savings") };
        Render(card, window, accounts);
        var before = card.Selected;

        // Act
        Find<ListBox>(card, "AccountsList").SelectedIndex = 1;

        // Assert
        Assert.Null(before);
        Assert.Same(accounts[1], card.Selected);
    }

    [AvaloniaFact]
    public void Row_DoubleClicked_RaisesEditRequestedForThatAccount()
    {
        // Arrange
        var (window, card) = Show();
        var everyday = Account("a", "Everyday");
        Render(card, window, [everyday]);
        var list = Find<ListBox>(card, "AccountsList");
        var row = (ListBoxItem)list.ContainerFromIndex(0)!;
        var point = row.TranslatePoint(new Point(row.Bounds.Width * 0.9, row.Bounds.Height / 2), window)!.Value;
        Account? edited = null;
        card.EditRequested += account => edited = account;

        // Act
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);

        // Assert
        Assert.Same(everyday, edited);
    }
}
