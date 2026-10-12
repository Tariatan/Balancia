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
            Title = Get(title), Heading = { Text = Get("The operation could not be completed.") }, MessageText =
            {
                Text = message
            },
            CloseButton = { Content = Get("Close") }
        };
        dialog.CloseButton.Click += (_, _) => dialog.Close();
        return dialog.ShowDialog(owner);
    }
}
