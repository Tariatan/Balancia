using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;

namespace Balancia.Desktop.Tests;

/// <summary>Drives a shown <see cref="MainWindow"/> through its real controls, the way a user would.</summary>
internal static class WindowDriver
{
    public static T Find<T>(this Window window, string name) where T : Control =>
        window.FindControl<T>(name) ?? throw new InvalidOperationException($"Control '{name}' was not found.");

    public static void Click(this Window window, string buttonName) =>
        window.Find<Button>(buttonName).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    public static void Press(this Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    /// <summary>Raises a key press as if it came from <paramref name="source"/> (the window when null) and bubbles to the window.</summary>
    public static void PressKey(this Window window, Key key, KeyModifiers modifiers = KeyModifiers.None, Control? source = null)
    {
        var target = source ?? window;
        target.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = key,
            KeyModifiers = modifiers,
            Source = target,
        });
    }

    /// <summary>Types a value into a text box and confirms it with Enter, which applies the filter.</summary>
    public static void EnterText(this Window window, string textBoxName, string text)
    {
        var box = window.Find<TextBox>(textBoxName);
        box.Text = text;
        window.PressKey(Key.Enter, source: box);
    }

    /// <summary>Waits for a dialog owned by the window, such as the remove confirmation.</summary>
    public static async Task<Window> WaitForDialogAsync(this Window owner, string title)
    {
        await DesktopWorkspace.WaitUntilAsync(() => owner.OwnedWindows.Any(w => w.Title == title), $"the '{title}' dialog");
        return owner.OwnedWindows.First(w => w.Title == title);
    }

    public static void ClickDialogButton(this Window dialog, string label)
    {
        var button = dialog.GetLogicalDescendants().OfType<Button>().FirstOrDefault(b => b.Content as string == label)
            ?? throw new InvalidOperationException($"Dialog '{dialog.Title}' has no '{label}' button.");
        button.Press();
    }

    /// <summary>Lets queued UI work run, for asserting that nothing happened.</summary>
    public static async Task SettleAsync()
    {
        for (var pass = 0; pass < 10; pass++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
    }

    public static Task WaitForTransactionsAsync(this Window window, int count, string description) =>
        DesktopWorkspace.WaitUntilAsync(() => window.TransactionCount() == count, description);

    /// <summary>Switches the period filter to "All" and waits until the list holds <paramref name="expectedCount"/> rows.</summary>
    public static async Task ShowAllTransactionsAsync(this Window window, int expectedCount)
    {
        window.Click("FilterButtonAll");
        await window.WaitForTransactionsAsync(expectedCount, $"all {expectedCount} transactions");
    }

    public static int TransactionCount(this Window window) => window.Find<ListBox>("TransactionsList").ItemCount;

    public static string TransactionsHeading(this Window window) => window.Find<TextBlock>("TransactionsHeading").Text ?? "";
}
