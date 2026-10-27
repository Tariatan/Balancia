using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Balancia.Storage;
using Serilog;

using static Balancia.Desktop.FormControls;
using static Balancia.Desktop.Localization.UiText;

namespace Balancia.Desktop;

public partial class MainWindow
{
    internal static string FriendlyError(Exception ex)
    {
        var english = ex switch
        {
            _ when LedgerFailures.IsConstraintViolation(ex) => "This change conflicts with existing data. Check names and referenced accounts/categories.",
            _ when LedgerFailures.IsDatabaseFailure(ex) => "The database could not be read or saved. Close other Balancia windows and try again.",
            OverflowException => "This amount or resulting total is outside the supported range.",
            FormatException => "Check the date (YYYY-MM-DD) and amount (for example 12.50).",
            IOException or UnauthorizedAccessException => "The local data folder is unavailable or not writable.",
            InvalidDataException or ArgumentException or InvalidOperationException => ex.Message.Split('\n')[0].TrimEnd('\r'),
            _ => "The operation failed. Your entered values have been kept; try again."
        };
        return Get(english);
    }

    internal Task ShowErrorDialog(string title, string message) => ErrorDialog.Show(this, title, message);

    private Task SelectFirst()
    {
        SetStatus("Select a row first.");
        return Task.CompletedTask;
    }

    private Window BuildDialogWindow(string title, DialogSize size)
    {
        var (height, minWidth, minHeight) = size switch
        {
            DialogSize.Compact => (200, 430, 240),
            DialogSize.Wide => (200, 900, 240),
            DialogSize.Tall => (730, 430, 360),
            _ => (480, 430, 360)
        };
        return new Window
        {
            Title = title,
            Icon = Icon,
            ShowInTaskbar = false,
            Width = 530,
            Height = height,
            MinWidth = minWidth,
            MinHeight = minHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brushes.White
        };
    }

    // prepareSave snapshots inputs on the UI thread; the returned action performs the complete write off-thread.
    private async Task EditDialog(string title, Control[] fields, Func<Action> prepareSave, DialogOptions? options = null)
    {
        options ??= new DialogOptions();
        var (size, saveLabel, initialFocus, onSaveAndContinue, validate) = options;
        var localizedTitle = Get(title);
        var dialog = BuildDialogWindow(localizedTitle, size);
        var body = new StackPanel
        {
            Spacing = 12,
            Margin = new Thickness(24)
        };
        body.Children.Add(new TextBlock
        {
            Text = localizedTitle,
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
            Content = Get(saveLabel),
            IsDefault = !options.IsConfirmation && onSaveAndContinue is null
        };
        var saveAndContinue = onSaveAndContinue is null
            ? null
            : new Button
            {
                Content = Get("Add another transaction"),
                IsDefault = true
            };

        var cancel = new Button
        {
            Content = Get("Cancel"),
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
            if (fields is [StackPanel { Children: { Count: > 0 } children }, ..] && children[^1] is InputElement input)
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
                if (validate?.Invoke() is { } validationError)
                {
                    error.Text = Get(validationError);
                    return;
                }

                var action = prepareSave();
                saving = true;
                body.IsEnabled = false;
                error.Text = Get("Saving…");
                Log.Information("Saving dialog changes, Operation: '{Operation}'", title);
                await Task.Run(action);
                Log.Information("Dialog changes saved, Operation: '{Operation}'", title);
                if (continueEditing)
                {
                    await Run(Refresh, false);
                    await onSaveAndContinue!();
                    body.IsEnabled = true;
                    initialFocus?.Focus();
                    error.Text = "";
                }
                else
                {
                    saved = true;
                    saving = false;
                    dialog.Close();
                }
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "Desktop operation failed, Operation: '{Operation}'", title);
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
