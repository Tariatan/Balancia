using Avalonia.Controls;
using static Balancia.Desktop.Localization.UiText;

namespace Balancia.Desktop;

public partial class SnapshotPassphraseWindow : Window
{
    public SnapshotPassphraseWindow() => InitializeComponent();

    internal static Task<string?> Ask(Window owner, bool setup)
    {
        var dialog = new SnapshotPassphraseWindow();
        dialog.Title = dialog.heading.Text = Get(setup ? "Snapshot encryption" : "Unlock snapshot");
        dialog.explanation.Text = Get(setup
            ? "Keep this passphrase in a safe place. Windows remembers the key for automatic exports. Android asks once, then remembers it too. Older files keep their previous passphrase."
            : "Enter the passphrase used when this snapshot was exported.");
        dialog.passphraseLabel.Text = Get("Passphrase");
        dialog.confirmLabel.Text = Get("Confirm passphrase");
        dialog.confirmLabel.IsVisible = dialog.confirmation.IsVisible = setup;
        dialog.accept.Content = Get(setup ? "Save" : "Unlock");
        dialog.cancel.Content = Get("Cancel");
        dialog.cancel.Click += (_, _) => dialog.Close(null);
        dialog.accept.Click += (_, _) =>
        {
            var value = dialog.passphrase.Text ?? "";
            if (setup && value.Length < 12)
            {
                dialog.error.Text = Get("Use at least 12 characters for the passphrase.");
                return;
            }

            if (string.IsNullOrEmpty(value) || setup && value != dialog.confirmation.Text)
            {
                dialog.error.Text = Get("The passphrases do not match or are empty.");
                return;
            }

            dialog.Close(value);
        };
        dialog.Opened += (_, _) => dialog.passphrase.Focus();
        dialog.Closed += (_, _) => dialog.passphrase.Text = dialog.confirmation.Text = "";
        return dialog.ShowDialog<string?>(owner);
    }
}
