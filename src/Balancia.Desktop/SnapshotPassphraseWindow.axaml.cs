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
        ConfirmLabel.Text = Get("Confirm passphrase");
        ConfirmLabel.IsVisible = Confirmation.IsVisible = setup;
        Accept.Content = Get(setup ? "Save" : "Unlock");
        Cancel.Content = Get("Cancel");
    }

    internal static Task<string?> Ask(Window owner, bool setup) =>
        new SnapshotPassphraseWindow(setup).ShowDialog<string?>(owner);

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

    private void OnOpened(object? sender, EventArgs e) => Passphrase.Focus();

    private void OnClosed(object? sender, EventArgs e) => Passphrase.Text = Confirmation.Text = string.Empty;
}
