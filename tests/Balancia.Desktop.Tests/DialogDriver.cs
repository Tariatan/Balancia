using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;

namespace Balancia.Desktop.Tests;

/// <summary>Fills in and submits the edit dialogs by their visible field labels, the way a user would.</summary>
internal static class DialogDriver
{
    /// <summary>Finds a dialog input by the label shown above it.</summary>
    public static Control Field(this Window dialog, string label) =>
        dialog.GetLogicalDescendants().OfType<Control>().FirstOrDefault(control => AutomationProperties.GetName(control) == label)
        ?? throw new InvalidOperationException($"Dialog '{dialog.Title}' has no field '{label}'.");

    /// <summary>Sets a field: text boxes (also inside the description and category pickers), date pickers and combo boxes.</summary>
    public static void Fill(this Window dialog, string label, string value)
    {
        switch (dialog.Field(label))
        {
            case TextBox box:
                box.Text = value;
                break;
            case CalendarDatePicker picker:
                picker.SelectedDate = DateTime.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture);
                break;
            case ComboBox combo:
                combo.SelectedItem = combo.ItemsSource!.Cast<object>().FirstOrDefault(item => item.ToString() == value)
                    ?? throw new InvalidOperationException($"Field '{label}' has no option '{value}'.");
                break;
            case var container:
                container.GetLogicalDescendants().OfType<TextBox>().First().Text = value;
                break;
        }
    }

    public static string TextOf(this Window dialog, string label) => dialog.Field(label) switch
    {
        TextBox box => box.Text ?? "",
        CalendarDatePicker picker => picker.SelectedDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
        ComboBox combo => combo.SelectedItem?.ToString() ?? "",
        var container => container.GetLogicalDescendants().OfType<TextBox>().First().Text ?? "",
    };

    /// <summary>The red validation or failure message at the bottom of a dialog; empty when there is none.</summary>
    public static string ErrorText(this Window dialog) =>
        dialog.GetLogicalDescendants().OfType<TextBlock>().FirstOrDefault(block => ReferenceEquals(block.Foreground, Brushes.DarkRed))?.Text ?? "";

    public static async Task WaitUntilClosedAsync(this Window owner) =>
        await DesktopWorkspace.WaitUntilAsync(() => owner.OwnedWindows.Count == 0, "the dialog to close");

    /// <summary>Opens a dialog from a panel button, fills it in with <paramref name="fill"/>, and returns it without saving.</summary>
    public static async Task<Window> OpenDialogAsync(this Window owner, string buttonName, string title)
    {
        owner.Click(buttonName);
        return await owner.WaitForDialogAsync(title);
    }

    /// <summary>Clicks the dialog's save button and waits for it to close.</summary>
    public static async Task SaveAndCloseAsync(this Window dialog, Window owner, string saveLabel = "Save")
    {
        dialog.ClickDialogButton(saveLabel);
        await owner.WaitUntilClosedAsync();
    }

    /// <summary>Clicks the dialog's save button and waits for its error message, leaving the dialog open.</summary>
    public static async Task<string> SaveExpectingErrorAsync(this Window dialog, string saveLabel = "Save")
    {
        dialog.ClickDialogButton(saveLabel);
        await DesktopWorkspace.WaitUntilAsync(() => dialog.ErrorText().Length > 0 && dialog.ErrorText() != "Saving…", "the dialog to report an error");
        return dialog.ErrorText();
    }

    public static async Task CancelAsync(this Window dialog, Window owner)
    {
        dialog.ClickDialogButton("Cancel");
        await owner.WaitUntilClosedAsync();
    }

    /// <summary>Double-clicks a list row with real pointer events, which opens its edit dialog.</summary>
    public static void DoubleClickRow(this Window window, ListBox list, int index)
    {
        var row = list.ContainerFromIndex(index) ?? throw new InvalidOperationException($"List has no row {index}.");
        var center = row.TranslatePoint(new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), window)
            ?? throw new InvalidOperationException("The row is not part of the window.");
        for (var click = 0; click < 2; click++)
        {
            window.MouseDown(center, MouseButton.Left);
            window.MouseUp(center, MouseButton.Left);
        }
    }
}
