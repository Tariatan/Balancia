using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Xunit;

using static Balancia.Desktop.FormControls;
using static Balancia.Desktop.Localization.UiText;

namespace Balancia.Desktop.Tests;

public sealed class EditDialogTests
{
    private const string Title = "Edit thing";

    private sealed class Probe
    {
        public List<string> Events { get; } = [];
        public int AfterSave { get; set; }
        public int WhileOpen { get; set; }
    }

    private static (Window Owner, EditDialog Dialog, Probe Probe) Create()
    {
        SetLanguage("en");
        var probe = new Probe();
        var owner = new Window { Width = 800, Height = 600 };
        owner.Show();
        var dialog = new EditDialog(owner,
            () =>
            {
                probe.AfterSave++;
                probe.Events.Add("refresh-after");
                return Task.CompletedTask;
            },
            () =>
            {
                probe.WhileOpen++;
                probe.Events.Add("refresh-while-open");
                return Task.CompletedTask;
            });
        return (owner, dialog, probe);
    }

    private static async Task<(Window Dialog, Task Shown)> OpenAsync(Window owner, EditDialog dialog, Func<Action> prepare,
        DialogOptions? options = null, Control[]? fields = null, string title = Title)
    {
        var shown = dialog.ShowAsync(title, fields ?? [Field("Name", Input("Anna"))], prepare, options);
        return (await owner.WaitForDialogAsync(Get(title)), shown);
    }

    private static Func<Action> Writes(Probe probe, string name = "write") => () =>
    {
        probe.Events.Add("prepare");
        return () => probe.Events.Add(name);
    };

    private static Button ButtonLabelled(Window dialog, string label) =>
        dialog.GetLogicalDescendants().OfType<Button>().Single(button => button.Content as string == label);

    [AvaloniaFact]
    public async Task Save_RunsTheWriteOnceClosesTheDialogAndRefreshesAfterwards()
    {
        // Arrange
        var (owner, edit, probe) = Create();
        var (dialog, shown) = await OpenAsync(owner, edit, Writes(probe));

        // Act
        dialog.ClickDialogButton("Save");
        await shown.CompleteAsync();

        // Assert
        Assert.Equal(["prepare", "write", "refresh-after"], probe.Events);
        Assert.Empty(owner.OwnedWindows);
    }

    [AvaloniaFact]
    public async Task Cancel_ClosesWithoutPreparingWritingOrRefreshing()
    {
        // Arrange
        var (owner, edit, probe) = Create();
        var (dialog, shown) = await OpenAsync(owner, edit, Writes(probe));

        // Act
        dialog.ClickDialogButton("Cancel");
        await shown.CompleteAsync();

        // Assert
        Assert.Empty(probe.Events);
    }

    [AvaloniaFact]
    public async Task Escape_ClosesWithoutSaving()
    {
        // Arrange
        var (owner, edit, probe) = Create();
        var (dialog, shown) = await OpenAsync(owner, edit, Writes(probe));

        // Act
        dialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
        await shown.CompleteAsync();

        // Assert
        Assert.Empty(probe.Events);
    }

    [AvaloniaFact]
    public async Task Save_WhenValidationFails_ShowsTheMessageKeepsTheDialogOpenAndSavesNothing()
    {
        // Arrange
        var (owner, edit, probe) = Create();
        var valid = false;
        var (dialog, shown) = await OpenAsync(owner, edit, Writes(probe),
            new DialogOptions(Validate: () => valid ? null : "Enter a description."));

        // Act
        dialog.ClickDialogButton("Save");

        // Assert
        Assert.Equal(Get("Enter a description."), dialog.ErrorText());
        Assert.Single(owner.OwnedWindows);
        Assert.Empty(probe.Events);

        // Act again, once the form is valid
        valid = true;
        dialog.ClickDialogButton("Save");
        await shown.CompleteAsync();
        Assert.Equal(["prepare", "write", "refresh-after"], probe.Events);
    }

    [AvaloniaFact]
    public async Task Save_WhenTheWriteFails_ShowsTheFirstLineKeepsTheValuesAndAllowsARetry()
    {
        // Arrange
        var (owner, edit, probe) = Create();
        var attempts = 0;
        var (dialog, shown) = await OpenAsync(owner, edit, () => () =>
        {
            attempts++;
            probe.Events.Add($"write{attempts}");
            if (attempts == 1)
            {
                throw new InvalidOperationException("Name already used\nsecond line");
            }
        });

        // Act
        var error = await dialog.SaveExpectingErrorAsync();

        // Assert
        Assert.Equal("Name already used", error);
        Assert.Single(owner.OwnedWindows);
        Assert.True(dialog.Field("Name").IsEffectivelyEnabled);
        Assert.Equal("Anna", dialog.TextOf("Name"));
        Assert.Equal(0, probe.AfterSave);

        // Act again
        dialog.ClickDialogButton("Save");
        await shown.CompleteAsync();
        Assert.Equal(["write1", "write2", "refresh-after"], probe.Events);
    }

    [AvaloniaFact]
    public async Task Save_WhenAValueCannotBeRead_ShowsTheFormatMessageAndWritesNothing()
    {
        // Arrange
        var (owner, edit, probe) = Create();
        var (dialog, _) = await OpenAsync(owner, edit, () => throw new FormatException("Select a date."));

        // Act
        var error = await dialog.SaveExpectingErrorAsync();

        // Assert
        Assert.Equal(Get("Check the date (YYYY-MM-DD) and amount (for example 12.50)."), error);
        Assert.Single(owner.OwnedWindows);
        Assert.Empty(probe.Events);
    }

    [AvaloniaFact]
    public async Task AddAnother_WritesRefreshesWhileOpenThenResetsAndStaysOpen()
    {
        // Arrange
        var (owner, edit, probe) = Create();
        var (dialog, shown) = await OpenAsync(owner, edit, Writes(probe),
            new DialogOptions(OnSaveAndContinue: () =>
            {
                probe.Events.Add("continue");
                return Task.CompletedTask;
            }));

        // Act
        dialog.ClickDialogButton("Add another transaction");
        await DesktopWorkspace.WaitUntilAsync(() => probe.Events.Contains("continue") && dialog.Field("Name").IsEffectivelyEnabled && dialog.ErrorText() == "",
            "the dialog to be ready for the next entry");

        // Assert
        Assert.Equal(["prepare", "write", "refresh-while-open", "continue"], probe.Events);
        Assert.Equal(0, probe.AfterSave);
        Assert.Single(owner.OwnedWindows);

        // Act again: the final save closes it and refreshes once
        dialog.ClickDialogButton("Save");
        await shown.CompleteAsync();
        Assert.Equal(1, probe.AfterSave);
    }

    [AvaloniaFact]
    public async Task AddAnother_ReturnsFocusToTheRequestedInput()
    {
        // Arrange
        var (owner, edit, probe) = Create();
        var first = Input("a");
        var second = Input("b");
        var (dialog, _) = await OpenAsync(owner, edit, Writes(probe),
            new DialogOptions(InitialFocus: second, OnSaveAndContinue: () =>
            {
                probe.Events.Add("continue");
                return Task.CompletedTask;
            }),
            [Field("First", first), Field("Second", second)]);
        await DesktopWorkspace.WaitUntilAsync(() => second.IsFocused, "the requested input to be focused");
        first.Focus();
        await DesktopWorkspace.WaitUntilAsync(() => first.IsFocused, "the first input to take the focus");

        // Act
        dialog.ClickDialogButton("Add another transaction");

        // Assert
        await DesktopWorkspace.WaitUntilAsync(() => probe.Events.Contains("continue") && second.IsFocused, "the requested input to be focused again");
        Assert.False(first.IsFocused);
    }

    [AvaloniaTheory]
    [InlineData(false, false, true, false)] // a plain form: Save is the default
    [InlineData(true, false, false, false)] // a confirmation: never the default
    [InlineData(false, true, false, true)] // with Add another: that button is the default
    public async Task DefaultButton_IsTheSaveButtonOnlyForAPlainForm(bool confirmation, bool withContinue, bool saveIsDefault, bool continueIsDefault)
    {
        // Arrange
        var (owner, edit, probe) = Create();
        var options = confirmation
            ? DialogOptions.Confirmation("Delete")
            : new DialogOptions(OnSaveAndContinue: withContinue ? () => Task.CompletedTask : null);

        // Act
        var (dialog, _) = await OpenAsync(owner, edit, Writes(probe), options);

        // Assert
        Assert.Equal(saveIsDefault, ButtonLabelled(dialog, confirmation ? "Delete" : "Save").IsDefault);
        if (withContinue)
        {
            Assert.Equal(continueIsDefault, ButtonLabelled(dialog, "Add another transaction").IsDefault);
        }
    }

    [AvaloniaFact]
    public async Task WhileSaving_TheDialogShowsProgressIgnoresASecondSaveAndCannotBeClosed()
    {
        // Arrange
        var (owner, edit, probe) = Create();
        var release = new ManualResetEventSlim();
        var writes = 0;
        var (dialog, shown) = await OpenAsync(owner, edit, () => () =>
        {
            Interlocked.Increment(ref writes);
            release.Wait();
        });

        // Act
        dialog.ClickDialogButton("Save");
        await DesktopWorkspace.WaitUntilAsync(() => dialog.ErrorText() == Get("Saving…"), "the save to start");
        var enabledWhileSaving = dialog.Field("Name").IsEffectivelyEnabled;
        dialog.ClickDialogButton("Save");
        dialog.Close();
        dialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
        var stillOpen = owner.OwnedWindows.Count == 1;
        release.Set();
        await shown.CompleteAsync();

        // Assert
        Assert.False(enabledWhileSaving);
        Assert.True(stillOpen);
        Assert.Equal(1, writes);
        Assert.Equal(1, probe.AfterSave);
    }

    [AvaloniaFact]
    public async Task Dialog_ShowsTheLocalizedTitleAsWindowTitleAndHeading()
    {
        // Arrange
        var (owner, edit, probe) = Create();

        // Act
        var (dialog, _) = await OpenAsync(owner, edit, Writes(probe), title: "Add account");

        // Assert
        Assert.Equal(Get("Add account"), dialog.Title);
        Assert.Contains(dialog.GetLogicalDescendants().OfType<TextBlock>(), block => block.Text == Get("Add account") && block.FontSize == 24);
    }

    [AvaloniaTheory]
    [InlineData("Compact", 240, 430)]
    [InlineData("Wide", 240, 900)]
    [InlineData("Standard", 480, 430)]
    [InlineData("Tall", 730, 430)]
    public async Task Dialog_SizeFollowsTheRequestedKind(string size, double height, double minWidth)
    {
        // Arrange
        var (owner, edit, probe) = Create();

        // Act
        var (dialog, _) = await OpenAsync(owner, edit, Writes(probe), new DialogOptions(Enum.Parse<DialogSize>(size)));

        // Assert
        Assert.Equal(height, dialog.Height);
        Assert.Equal(minWidth, dialog.MinWidth);
        Assert.Equal(Math.Max(530, minWidth), dialog.Width);
    }

    [AvaloniaFact]
    public async Task Opening_FocusesTheRequestedInput()
    {
        // Arrange
        var (owner, edit, probe) = Create();
        var first = Input("a");
        var second = Input("b");

        // Act
        var (dialog, _) = await OpenAsync(owner, edit, Writes(probe), new DialogOptions(InitialFocus: second),
            [Field("First", first), Field("Second", second)]);
        await DesktopWorkspace.WaitUntilAsync(() => second.IsFocused, "the requested input to be focused");

        // Assert
        Assert.False(first.IsFocused);
    }

    [AvaloniaFact]
    public async Task Opening_WithoutARequestedInput_FocusesTheInputOfTheFirstField()
    {
        // Arrange
        var (owner, edit, probe) = Create();
        var first = Input("a");

        // Act
        var (dialog, _) = await OpenAsync(owner, edit, Writes(probe), fields: [Field("First", first), Field("Second", Input("b"))]);

        // Assert
        await DesktopWorkspace.WaitUntilAsync(() => first.IsFocused, "the first input to be focused");
    }
}
