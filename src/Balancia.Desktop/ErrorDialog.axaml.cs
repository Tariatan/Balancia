using Avalonia.Controls;

using static Balancia.Desktop.Localization.UiText;

namespace Balancia.Desktop;

public partial class ErrorDialog : Window
{
    public ErrorDialog()
    {
        InitializeComponent();
    }

    public static Task Show(Window owner, string title, string message)
    {
        var dialog = new ErrorDialog
        {
            Title = Get(title)
        };
        dialog.heading.Text = Get("The operation could not be completed.");
        dialog.messageText.Text = message;
        dialog.closeButton.Content = Get("Close");
        dialog.closeButton.Click += (_, _) => dialog.Close();
        return dialog.ShowDialog(owner);
    }
}
