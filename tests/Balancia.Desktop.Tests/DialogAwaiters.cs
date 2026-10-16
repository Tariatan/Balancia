using Avalonia.Controls;

namespace Balancia.Desktop.Tests;

/// <summary>Finds dialogs by type and drives async window operations that stay open while a dialog is shown.</summary>
internal static class DialogAwaiters
{
    public static async Task<T> WaitForDialogAsync<T>(this Window owner) where T : Window
    {
        await DesktopWorkspace.WaitUntilAsync(() => owner.OwnedWindows.OfType<T>().Any(), $"a {typeof(T).Name}");
        return owner.OwnedWindows.OfType<T>().First();
    }

    /// <summary>Waits for a window operation, pumping the dispatcher meanwhile.</summary>
    public static async Task CompleteAsync(this Task operation)
    {
        await DesktopWorkspace.WaitUntilAsync(() => operation.IsCompleted, "the operation to finish");
        await operation;
    }

    public static string Text(this Window window, string name) => window.Find<TextBlock>(name).Text ?? "";

    /// <summary>Fills the passphrase dialog (and its confirmation when it asks for one) and presses its accept button.</summary>
    public static void SubmitPassphrase(this Window dialog, string passphrase, string acceptLabel, string? confirmation = null)
    {
        dialog.Find<TextBox>("Passphrase").Text = passphrase;
        dialog.Find<TextBox>("Confirmation").Text = confirmation ?? passphrase;
        dialog.ClickDialogButton(acceptLabel);
    }
}
