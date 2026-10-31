using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Xunit;

namespace Balancia.Desktop.Tests;

public sealed class AmountTextBoxTests
{
    [AvaloniaTheory]
    [InlineData("a")]
    [InlineData("asdfa2323")]
    [InlineData("-1")]
    [InlineData("1+2")]
    [InlineData(".")]
    [InlineData(",")]
    public void KeyTextInput_InvalidInsertion_PreservesTextAndSelection(string input)
    {
        // Arrange
        var amount = new AmountTextBox { Text = "12.50" };
        var window = new Window { Content = amount };
        window.Show();
        amount.Focus();
        amount.CaretIndex = 2;
        var selection = (amount.SelectionStart, amount.SelectionEnd);
        var changes = 0;
        amount.PropertyChanged += (_, e) => changes += e.Property == TextBox.TextProperty ? 1 : 0;

        try
        {
            // Act
            window.KeyTextInput(input);

            // Assert
            Assert.Equal("12.50", amount.Text);
            Assert.Equal(selection, (amount.SelectionStart, amount.SelectionEnd));
            Assert.Equal(0, changes);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData("12.50", 0, 5, "3,25", "3,25")]
    [InlineData("12.50", 3, 2, ",", "12,50")]
    [InlineData("12", 2, 2, ".50", "12.50")]
    [InlineData("", 0, 0, ",", ",")]
    public void KeyTextInput_ValidInsertion_ReplacesSelection(string text, int start, int end, string input, string expected)
    {
        // Arrange
        var amount = new AmountTextBox { Text = text };
        var window = new Window { Content = amount };
        window.Show();
        amount.Focus();
        amount.SelectionStart = start;
        amount.SelectionEnd = end;

        try
        {
            // Act
            window.KeyTextInput(input);

            // Assert
            Assert.Equal(expected, amount.Text);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData("asdfa2323", "12.50")]
    [InlineData("1.2.3", "12.50")]
    [InlineData("-5", "12.50")]
    [InlineData(" 3,25 ", "3,25")]
    [InlineData("3.25", "3.25")]
    public async Task Paste_ValidatesBeforeReplacingSelection(string pasted, string expected)
    {
        // Arrange
        var amount = new AmountTextBox { Text = "12.50" };
        var window = new Window { Content = amount };
        window.Show();
        amount.Focus();
        amount.SelectAll();
        await window.Clipboard!.SetTextAsync(pasted);

        try
        {
            // Act
            amount.Paste();
            await Task.Yield();
            Dispatcher.UIThread.RunJobs();

            // Assert
            Assert.Equal(expected, amount.Text);
        }
        finally
        {
            window.Close();
        }
    }
}
