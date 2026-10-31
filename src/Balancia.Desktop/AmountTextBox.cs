using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Serilog;

namespace Balancia.Desktop;

/// <summary>Rejects nonnumeric typing and paste before either changes the amount.</summary>
internal sealed class AmountTextBox : TextBox
{
    public AmountTextBox()
    {
        PastingFromClipboard += OnPasting;
        DragDrop.SetAllowDrop(this, false);
        InputMethod.SetIsInputMethodEnabled(this, false);
    }

    protected override Type StyleKeyOverride => typeof(TextBox);

    protected override void OnTextInput(TextInputEventArgs e)
    {
        if (!CanInsert(e.Text))
        {
            e.Handled = true;
            return;
        }

        base.OnTextInput(e);
    }

    private bool CanInsert(string? input)
    {
        var text = Text ?? string.Empty;
        var start = Math.Min(SelectionStart, SelectionEnd);
        var end = Math.Max(SelectionStart, SelectionEnd);
        var candidate = text.Remove(start, end - start).Insert(start, input ?? string.Empty);
        var hasSeparator = false;
        foreach (var character in candidate)
        {
            if (char.IsAsciiDigit(character))
            {
                continue;
            }

            if (character is '.' or ',' && !hasSeparator)
            {
                hasSeparator = true;
                continue;
            }

            return false;
        }

        return true;
    }

    private async void OnPasting(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (IsReadOnly || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
        {
            return;
        }

        try
        {
            var text = (await clipboard.TryGetTextAsync())?.Trim();
            if (!IsReadOnly && text is not null && CanInsert(text))
            {
                SelectedText = text;
            }
        }
        catch (Exception exception)
        {
            Log.ForContext<AmountTextBox>().Warning("Could not read amount from clipboard, FailureType: '{FailureType}'", exception.GetType().Name);
        }
    }
}
