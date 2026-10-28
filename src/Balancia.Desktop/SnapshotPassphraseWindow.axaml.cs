using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using static Balancia.Desktop.Localization.UiText;

namespace Balancia.Desktop;

public partial class SnapshotPassphraseWindow : Window
{
    private const int MinimumLength = 12;

    private readonly bool setup;

    public SnapshotPassphraseWindow() => InitializeComponent();

    private SnapshotPassphraseWindow(bool setup) : this()
    {
        this.setup = setup;
        Title = Heading.Text = Get(setup ? "Snapshot encryption" : "Unlock snapshot");
        Explanation.Text = Get(setup
            ? "Keep this passphrase in a safe place. Windows remembers the key for automatic exports. Android asks once, then remembers it too. Older files keep their previous passphrase."
            : "Enter the passphrase used when this snapshot was exported.");
        PassphraseLabel.Text = Get("Passphrase");
        SetPassphraseVisibility(false);
        ConfirmLabel.Text = Get("Confirm passphrase");
        ConfirmLabel.IsVisible = Confirmation.IsVisible = setup;
        Accept.Content = Get(setup ? "Save" : "Unlock");
        Cancel.Content = Get("Cancel");
    }

    /// <summary>Asks for the passphrase; null when canceled. <paramref name="error"/> is shown when the previous passphrase did not unlock the snapshot.</summary>
    internal static Task<string?> Ask(Window owner, bool setup, string? error = null)
    {
        var window = new SnapshotPassphraseWindow(setup);
        if (error is not null)
        {
            window.Error.Text = Get(error);
        }

        return window.ShowDialog<string?>(owner);
    }

    private void OnAccept(object? sender, RoutedEventArgs e)
    {
        var value = Passphrase.Text ?? string.Empty;

        if (setup && value.Length < MinimumLength)
        {
            Error.Text = Get("Use at least 12 characters for the passphrase.");
            return;
        }

        if (value.Length == 0 || setup && value != Confirmation.Text)
        {
            Error.Text = Get("The passphrases do not match or are empty.");
            return;
        }

        Close(value);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);

    private void OnShowPassphrase(object? sender, RoutedEventArgs e) =>
        SetPassphraseVisibility(ShowPassphrase.IsChecked == true);

    private void SetPassphraseVisibility(bool show)
    {
        Passphrase.RevealPassword = Confirmation.RevealPassword = show;
        EyeSlash.IsVisible = show;
        var action = Get(show ? "Hide" : "Show");
        ToolTip.SetTip(ShowPassphrase, action);
        AutomationProperties.SetName(ShowPassphrase, action);
    }

    private void OnOpened(object? sender, EventArgs e) => Passphrase.Focus();

    private void OnClosed(object? sender, EventArgs e) => Passphrase.Text = Confirmation.Text = string.Empty;
}
