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

public sealed class CategoriesCardTests
{
    private static Category Top(string id, string name, bool archived = false) => new(id, name, null, name, archived);

    private static Category Child(string id, string name, string parent, string parentName, bool archived = false) =>
        new(id, name, parent, $"{parentName} / {name}", archived);

    private static (Window Window, CategoriesCard Card) Show()
    {
        var card = new CategoriesCard();
        var window = new Window { Width = 420, Height = 360, Content = card };
        window.Show();
        return (window, card);
    }

    private static T Find<T>(CategoriesCard card, string name) where T : Control => card.FindControl<T>(name)!;

    private static CheckBox[] Checks(CategoriesCard card) =>
        [.. Find<ListBox>(card, "CategoriesList").GetLogicalDescendants().OfType<CheckBox>()];

    private static CheckBox CheckFor(CategoriesCard card, string path) =>
        Checks(card).Single(check => AutomationProperties.GetName(check) == path);

    private static void Render(CategoriesCard card, Window window, IReadOnlyList<Category> categories, Func<string, bool>? isSelected = null)
    {
        card.Render(categories, isSelected ?? (_ => false));
        window.UpdateLayout();
    }

    private static ListBoxItem Row(CategoriesCard card, int index) =>
        (ListBoxItem)Find<ListBox>(card, "CategoriesList").ContainerFromIndex(index)!;

    private static readonly Category Food = Top("food", "Food");
    private static readonly Category Lunch = Child("lunch", "Lunch", "food", "Food");
    private static readonly Category Travel = Top("travel", "Travel");

    [AvaloniaFact]
    public void Render_WithCategories_ListsThemAllWithTheirCheckboxes()
    {
        // Arrange
        var (window, card) = Show();

        // Act
        Render(card, window, [Food, Lunch, Travel]);

        // Assert
        Assert.Equal(3, Find<ListBox>(card, "CategoriesList").ItemCount);
        Assert.Equal(["Food", "Food / Lunch", "Travel"], Checks(card).Select(AutomationProperties.GetName));
    }

    [AvaloniaFact]
    public void Rows_AreTintedByTopLevelGroupWithSubcategoriesSharingTheirParents()
    {
        // Arrange
        var (window, card) = Show();

        // Act
        Render(card, window, [Food, Lunch, Travel]);

        // Assert
        Assert.Same(RowBrushes.Ivory, Row(card, 0).Background);
        Assert.Same(RowBrushes.Ivory, Row(card, 1).Background);
        Assert.Same(RowBrushes.Blue, Row(card, 2).Background);
    }

    [AvaloniaFact]
    public void Subcategory_Row_IsIndentedAndTopLevelIsNot()
    {
        // Arrange
        var (window, card) = Show();

        // Act
        Render(card, window, [Food, Lunch]);

        // Assert
        Assert.Equal(0, CheckFor(card, "Food").GetLogicalParent<Grid>()!.Margin.Left);
        Assert.Equal(15, CheckFor(card, "Food / Lunch").GetLogicalParent<Grid>()!.Margin.Left);
    }

    [AvaloniaFact]
    public void Row_OfAnArchivedCategory_ShowsAndAnnouncesTheArchivedMarker()
    {
        // Arrange
        var (window, card) = Show();
        var old = Top("old", "Old", archived: true);

        // Act
        Render(card, window, [Food, old]);

        // Assert
        var shown = CheckFor(card, "Old").GetLogicalParent<Grid>()!.Children.OfType<TextBlock>().Single().Text;
        Assert.Equal(ArchivedLabel.Of("Old", true), shown);
        Assert.NotEqual("Old", shown);
        Assert.Equal(ArchivedLabel.Of("Old", true), ControlAutomationPeer.CreatePeerForElement(Row(card, 1)).GetName());
    }

    [AvaloniaFact]
    public void Row_AccessibilityName_IsTheCategoryPath()
    {
        // Arrange
        var (window, card) = Show();

        // Act
        Render(card, window, [Food, Lunch]);

        // Assert
        Assert.Equal("Food / Lunch", ControlAutomationPeer.CreatePeerForElement(Row(card, 1)).GetName());
    }

    [AvaloniaFact]
    public void Render_Again_ReplacesTheRowsAndTheirCheckboxes()
    {
        // Arrange
        var (window, card) = Show();
        Render(card, window, [Food, Lunch, Travel]);

        // Act
        Render(card, window, [Travel]);

        // Assert
        Assert.Equal(1, Find<ListBox>(card, "CategoriesList").ItemCount);
        Assert.Equal(["Travel"], Checks(card).Select(AutomationProperties.GetName));
    }

    [AvaloniaFact]
    public void Checkboxes_FollowTheSelectionPredicate()
    {
        // Arrange
        var (window, card) = Show();

        // Act
        Render(card, window, [Food, Lunch, Travel], isSelected: id => id is "lunch" or "travel");

        // Assert
        Assert.False(CheckFor(card, "Food").IsChecked);
        Assert.True(CheckFor(card, "Food / Lunch").IsChecked);
        Assert.True(CheckFor(card, "Travel").IsChecked);
    }

    [AvaloniaFact]
    public void SyncFilterChecks_AfterTheFilterChanged_UpdatesTheBoxesWithoutRaisingToggles()
    {
        // Arrange
        var (window, card) = Show();
        var selected = new HashSet<string> { "travel" };
        Render(card, window, [Food, Lunch, Travel], isSelected: selected.Contains);
        var toggles = new List<(string Id, bool Selected)>();
        card.FilterToggled += (id, isSelected) => toggles.Add((id, isSelected));
        selected.Clear();
        selected.Add("food");

        // Act
        card.SyncFilterChecks();

        // Assert
        Assert.True(CheckFor(card, "Food").IsChecked);
        Assert.False(CheckFor(card, "Travel").IsChecked);
        Assert.Empty(toggles);
    }

    [AvaloniaFact]
    public void Checkbox_ChangedByTheUser_RaisesFilterToggledAndSelectsTheRow()
    {
        // Arrange
        var (window, card) = Show();
        Render(card, window, [Food, Lunch, Travel]);
        var toggles = new List<(string Id, bool Selected)>();
        card.FilterToggled += (id, isSelected) => toggles.Add((id, isSelected));

        // Act
        CheckFor(card, "Travel").IsChecked = true;

        // Assert
        Assert.Equal([("travel", true)], toggles);
        Assert.Same(Travel, card.Selected);
    }

    [AvaloniaFact]
    public void Buttons_Pressed_RaiseTheirRequests()
    {
        // Arrange
        var (_, card) = Show();
        var raised = new List<string>();
        card.AddRequested += () => raised.Add("add");
        card.SelectAllRequested += () => raised.Add("all");
        card.ArchiveRequested += () => raised.Add("archive");
        card.RemoveRequested += () => raised.Add("remove");

        // Act
        Find<Button>(card, "CategoryAddButton").Press();
        Find<Button>(card, "CategorySelectAllButton").Press();
        Find<Button>(card, "CategoryArchiveButton").Press();
        Find<Button>(card, "CategoryRemoveButton").Press();

        // Assert
        Assert.Equal(["add", "all", "archive", "remove"], raised);
    }

    [AvaloniaFact]
    public void Selected_NothingChosen_IsNullAndTheChosenRowOtherwise()
    {
        // Arrange
        var (window, card) = Show();
        Render(card, window, [Food, Lunch, Travel]);
        var before = card.Selected;

        // Act
        Find<ListBox>(card, "CategoriesList").SelectedIndex = 1;

        // Assert
        Assert.Null(before);
        Assert.Same(Lunch, card.Selected);
    }

    [AvaloniaFact]
    public void Row_DoubleClicked_RaisesEditRequestedForThatCategory()
    {
        // Arrange
        var (window, card) = Show();
        Render(card, window, [Food, Travel]);
        var row = Row(card, 1);
        var point = row.TranslatePoint(new Point(row.Bounds.Width * 0.9, row.Bounds.Height / 2), window)!.Value;
        Category? edited = null;
        card.EditRequested += category => edited = category;

        // Act
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);

        // Assert
        Assert.Same(Travel, edited);
    }
}
