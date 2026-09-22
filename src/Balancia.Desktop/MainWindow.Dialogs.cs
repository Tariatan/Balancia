using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Microsoft.Data.Sqlite;

namespace Balancia.Desktop;

public partial class MainWindow
{
    private static string FriendlyError(Exception ex) => ex switch
    {
        SqliteException { SqliteErrorCode: 19 } => "This change conflicts with existing data. Check names and referenced accounts/categories.",
        SqliteException => "The database could not be read or saved. Close other Balancia windows and try again.",
        OverflowException => "This amount or resulting total is outside the supported range.",
        FormatException => "Check the date (YYYY-MM-DD) and amount (for example 12.50).",
        IOException or UnauthorizedAccessException => "The local data folder is unavailable or not writable.",
        InvalidDataException => ex.Message,
        ArgumentException or InvalidOperationException => ex.Message,
        _ => "The operation failed. Your entered values have been kept; try again."
    };

    private async Task ShowErrorDialog(string title, string message)
    {
        var dialog = new Window
        {
            Title = title,
            Icon = Icon,
            ShowInTaskbar = false,
            Width = 520,
            Height = 260,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var close = new Button
        {
            Content = "Close",
            IsDefault = true,
            IsCancel = true
        };
        close.Click += (_, _) => dialog.Close();
        dialog.Content = new StackPanel
        {
            Spacing = 16,
            Margin = new Thickness(24),
            Children =
            {
                new TextBlock
                {
                    Text = "The operation could not be completed.",
                    FontSize = 20,
                    FontWeight = FontWeight.SemiBold
                },
                new TextBlock
                {
                    Text = message,
                    TextWrapping = TextWrapping.Wrap
                },
                close
            }
        };
        await dialog.ShowDialog(this);
    }

    private Task SelectFirst()
    {
        Status.Text = "Select a row first.";
        return Task.CompletedTask;
    }

    // Snapshot inputs on the UI thread, then perform the complete write off-thread.
    private async Task EditDialog(string title, Control[] fields, Func<Action> prepareSave, string saveLabel = "Save",
        InputElement? initialFocus = null, Action? onSaveAndContinue = null)
    {
        var isRemoval = saveLabel == "Remove";
        var dialog = new Window
        {
            Title = title,
            Icon = Icon,
            ShowInTaskbar = false,
            Width = 530,
            Height = isRemoval ? 200 : title.Contains("transaction", StringComparison.OrdinalIgnoreCase) ? 730 : 480,
            MinWidth = isRemoval ? 900 : 430,
            MinHeight = isRemoval ? 240 : 360,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brushes.White
        };
        var body = new StackPanel
        {
            Spacing = 12,
            Margin = new Thickness(24)
        };
        body.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 24,
            FontWeight = FontWeight.SemiBold
        });
        foreach (var field in fields)
        {
            body.Children.Add(field);
        }

        var error = Text("");
        error.Foreground = Brushes.DarkRed;
        body.Children.Add(error);
        var save = new Button
        {
            Content = saveLabel,
            IsDefault = saveLabel == "Save" && onSaveAndContinue is null
        };
        Button? saveAndContinue = null;
        if (onSaveAndContinue is not null)
        {
            saveAndContinue = new Button
            {
                Content = "Add another transaction",
                IsDefault = true
            };
        }

        var cancel = new Button
        {
            Content = "Cancel",
            IsCancel = true
        };
        body.Children.Add(saveAndContinue is null ? Row(save, cancel) : Row(save, saveAndContinue, cancel));
        dialog.Content = new ScrollViewer { Content = body };
        var saving = false;
        var saved = false;
        cancel.Click += (_, _) => dialog.Close();
        dialog.Closing += (_, e) =>
        {
            if (saving)
            {
                e.Cancel = true;
            }
        };
        dialog.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && !saving)
            {
                dialog.Close();
            }
        };

        save.Click += async (_, _) => await SaveAsync(false);
        if (saveAndContinue is not null)
        {
            saveAndContinue.Click += async (_, _) => await SaveAsync(true);
        }
        dialog.Opened += (_, _) =>
        {
            if (initialFocus is not null)
            {
                initialFocus.Focus();
                return;
            }
            if ((fields.Length > 0 ? fields[0] : null) is StackPanel panel &&
                (panel.Children.Count > 0 ? panel.Children[^1] : null) is InputElement input)
            {
                input.Focus();
            }
        };
        await dialog.ShowDialog(this);
        if (saved)
        {
            await Run(Refresh);
        }

        return;

        async Task SaveAsync(bool continueEditing)
        {
            if (saving)
            {
                return;
            }

            try
            {
                var action = prepareSave();
                saving = true;
                body.IsEnabled = false;
                error.Text = "Saving…";
                await Task.Run(action);
                saving = false;
                if (continueEditing)
                {
                    await Run(Refresh, false);
                    body.IsEnabled = true;
                    initialFocus?.Focus();
                    onSaveAndContinue!();
                    error.Text = "";
                }
                else
                {
                    saved = true;
                    dialog.Close();
                }
            }
            catch (Exception ex)
            {
                error.Text = FriendlyError(ex);
            }
            finally
            {
                saving = false;
                body.IsEnabled = true;
            }
        }
    }
}
