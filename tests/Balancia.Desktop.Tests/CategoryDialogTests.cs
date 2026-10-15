using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Balancia.Core;
using Xunit;

namespace Balancia.Desktop.Tests;

public sealed class CategoryDialogTests
{
    [AvaloniaFact]
    public async Task Add_TopLevelCategory_AppearsInTheList()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        var store = workspace.SeedStore();
        var window = await workspace.OpenWindowAsync();
        var dialog = await window.OpenDialogAsync("CategoryAddButton", "Add category");
        dialog.Fill("Name", "Food");

        // Act
        await dialog.SaveAndCloseAsync(window);
        await DesktopWorkspace.WaitUntilAsync(() => window.Find<ListBox>("CategoriesList").ItemCount == 1, "the category to appear");

        // Assert
        var category = Assert.Single(store.ReadSnapshot().Categories);
        Assert.Equal("Food", category.Name);
        Assert.Null(category.ParentId);
    }

    [AvaloniaFact]
    public async Task Add_Subcategory_IsFiledUnderItsParent()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        var store = workspace.SeedStore();
        var food = store.SaveCategory(null, "Food", null);
        var window = await workspace.OpenWindowAsync();
        var dialog = await window.OpenDialogAsync("CategoryAddButton", "Add category");
        dialog.Fill("Name", "Groceries");
        dialog.Fill("Parent category", "Food");

        // Act
        await dialog.SaveAndCloseAsync(window);

        // Assert
        var child = Assert.Single(store.ReadSnapshot().Categories, category => category.Name == "Groceries");
        Assert.Equal(food, child.ParentId);
        Assert.Equal("Food/Groceries", child.Path.Replace(" / ", "/"));
    }

    [AvaloniaTheory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Food/Groceries")]
    public async Task Add_WithAnInvalidName_ShowsAnErrorAndCreatesNothing(string name)
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        var store = workspace.SeedStore();
        var window = await workspace.OpenWindowAsync();
        var dialog = await window.OpenDialogAsync("CategoryAddButton", "Add category");
        dialog.Fill("Name", name);

        // Act
        var error = await dialog.SaveExpectingErrorAsync();

        // Assert
        Assert.Equal("Enter a category name without '/'; choose its parent separately.", error);
        Assert.Empty(store.ReadSnapshot().Categories);
    }

    [AvaloniaFact]
    public async Task Edit_ByDoubleClickingTheRow_RenamesTheCategory()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        var store = workspace.SeedStore();
        store.SaveCategory(null, "Food", null);
        var window = await workspace.OpenWindowAsync();
        var list = window.Find<ListBox>("CategoriesList");
        list.SelectedIndex = 0;

        // Act
        window.DoubleClickRow(list, 0);
        var dialog = await window.WaitForDialogAsync("Edit category");
        var shownName = dialog.TextOf("Name");
        dialog.Fill("Name", "Groceries");
        await dialog.SaveAndCloseAsync(window);

        // Assert
        Assert.Equal("Food", shownName);
        Assert.Equal("Groceries", Assert.Single(store.ReadSnapshot().Categories).Name);
    }

    [AvaloniaFact]
    public async Task Archive_SelectedCategory_MarksItArchived()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        var store = workspace.SeedStore();
        store.SaveCategory(null, "Food", null);
        var window = await workspace.OpenWindowAsync();
        window.Find<ListBox>("CategoriesList").SelectedIndex = 0;
        var dialog = await window.OpenDialogAsync("CategoryArchiveButton", "Archive category");

        // Act
        await dialog.SaveAndCloseAsync(window, "Archive");

        // Assert
        Assert.True(Assert.Single(store.ReadSnapshot().Categories).Archived);
    }

    [AvaloniaFact]
    public async Task Delete_UnusedCategory_RemovesIt()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        var store = workspace.SeedStore();
        store.SaveCategory(null, "Food", null);
        var window = await workspace.OpenWindowAsync();
        window.Find<ListBox>("CategoriesList").SelectedIndex = 0;
        var dialog = await window.OpenDialogAsync("CategoryRemoveButton", "Delete category");

        // Act
        await dialog.SaveAndCloseAsync(window, "Delete");

        // Assert
        Assert.Empty(store.ReadSnapshot().Categories);
    }

    [AvaloniaFact]
    public async Task Delete_CategoryWithSubcategories_IsRefused()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        var store = workspace.SeedStore();
        var food = store.SaveCategory(null, "Food", null);
        store.SaveCategory(null, "Groceries", food);
        var window = await workspace.OpenWindowAsync();
        var list = window.Find<ListBox>("CategoriesList");
        list.SelectedItem = list.Items.Cast<object>().First(item => item.ToString() == "Food");
        var dialog = await window.OpenDialogAsync("CategoryRemoveButton", "Delete category");

        // Act
        var error = await dialog.SaveExpectingErrorAsync("Delete");

        // Assert
        Assert.Equal("A category with subcategories cannot be deleted. Archive it instead.", error);
        Assert.Equal(2, store.ReadSnapshot().Categories.Count);
    }

    [AvaloniaFact]
    public async Task Delete_CategoryUsedByTransactions_IsRefused()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        var store = workspace.SeedStore();
        var food = store.SaveCategory(null, "Food", null);
        store.SaveTransaction(null, new TransactionDraft(TransactionKind.Expense, LedgerSeed.Today,
            "Lunch", Money.FromFrancs(5m), LedgerSeed.Account(store), CategoryId: food));
        var window = await workspace.OpenWindowAsync();
        window.Find<ListBox>("CategoriesList").SelectedIndex = 0;
        var dialog = await window.OpenDialogAsync("CategoryRemoveButton", "Delete category");

        // Act
        var error = await dialog.SaveExpectingErrorAsync("Delete");

        // Assert
        Assert.Equal("A category used by transactions cannot be deleted. Archive it instead.", error);
        Assert.Single(store.ReadSnapshot().Categories);
    }
}
