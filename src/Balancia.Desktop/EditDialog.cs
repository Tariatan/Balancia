using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Serilog;

using static Balancia.Desktop.FormControls;
using static Balancia.Desktop.Localization.UiText;

namespace Balancia.Desktop;

/// <summary>
/// Shows a modal form over <paramref name="owner"/> and saves it: it checks the form, takes a snapshot of the inputs, performs the write off the UI thread,
/// and shows a failure in the dialog so that the entered values are kept. After a save the overview is refreshed through the given callbacks.
/// </summary>
/// <param name="owner">The window the dialogs open over; its icon is used for them.</param>
/// <param name="refreshAfterSave">Refreshes the overview once a dialog that saved has closed.</param>
/// <param name="refreshWhileOpen">Refreshes the overview while the dialog stays open after "Add another", without disabling the window's controls.</param>
internal sealed class EditDialog(Window owner, Func<Task> refreshAfterSave, Func<Task> refreshWhileOpen)
{
    /// <summary>
    /// Shows the form and returns once it is closed. <paramref name="prepareSave"/> snapshots the inputs on the UI thread and returns the action that performs
    /// the complete write off-thread; it may throw <see cref="FormatException"/> for an unreadable value, which is shown in the dialog.
    /// </summary>
    public async Task ShowAsync(string title, Control[] fields, Func<Action> prepareSave, DialogOptions? options = null)
    {
        options ??= new DialogOptions();
        var (size, saveLabel, initialFocus, onSaveAndContinue, validate) = options;
        var localizedTitle = Get(title);
        var dialog = BuildWindow(localizedTitle, size);
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
        await dialog.ShowDialog(owner);
        if (saved)
        {
            await refreshAfterSave();
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
                    await refreshWhileOpen();
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
                Log.Error(ex, "Desktop operation failed, Operation: '{Operation}'", title);
                error.Text = FriendlyErrors.For(ex);
            }
            finally
            {
                saving = false;
                body.IsEnabled = true;
            }
        }
    }

    private Window BuildWindow(string title, DialogSize size)
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
            Icon = owner.Icon,
            ShowInTaskbar = false,
            Width = 530,
            Height = height,
            MinWidth = minWidth,
            MinHeight = minHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Palette.Surface
        };
    }
}
