using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Balancia.Core;
using Balancia.Storage;
using Microsoft.Data.Sqlite;

namespace Balancia.Desktop;

public partial class MainWindow
{
    private static void AddColumn(Grid grid, Control control, int column)
    {
        Grid.SetColumn(control, column);
        grid.Children.Add(control);
    }

    private static void AddRow(Grid grid, Control control, int row)
    {
        Grid.SetRow(control, row);
        grid.Children.Add(control);
    }

    private static Border Panel(Control content) => new()
    {
        Background = Brushes.White,
        BorderBrush = Brush.Parse("#DCE6EB"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(6),
        Padding = new Thickness(15),
        Child = content
    };

    private static TextBlock QuietText(string content, double size) => new()
    {
        Text = content,
        FontSize = size,
        Foreground = Brush.Parse("#71838D"),
        VerticalAlignment = VerticalAlignment.Center
    };

    private static TextBlock Heading(string content, double size) => new()
    {
        Text = content,
        FontSize = size,
        FontWeight = FontWeight.SemiBold,
        Foreground = Brush.Parse("#263C48"),
        Margin = new Thickness(0, 0, 0, 10)
    };

    private static IBrush BalanceColor(Money amount) => Brush.Parse(amount.Centimes > 0 ? "#2C8B6D" : "#B95D4D");

    private static Grid TwoColumn(string left, string right, double size, IBrush? valueColor = null)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(0, 4)
        };
        row.Children.Add(new TextBlock
        {
            Text = left,
            FontSize = size,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        var value = new TextBlock
        {
            Text = right,
            FontSize = size,
            FontWeight = FontWeight.SemiBold,
            Foreground = valueColor ?? Brush.Parse("#263C48"),
            Margin = new Thickness(8, 0, 0, 0)
        };
        AddColumn(row, value, 1);
        return row;
    }

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
                    Text = "The operation could not be completed.", FontSize = 20, FontWeight = FontWeight.SemiBold
                },
                new TextBlock
                {
                    Text = message, TextWrapping = TextWrapping.Wrap
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
    private async Task EditDialog(string title, Control[] fields, Func<Action> prepareSave, string saveLabel = "Save")
    {
        var dialog = new Window
        {
            Title = title,
            Width = 530,
            Height = title.Contains("transaction", StringComparison.OrdinalIgnoreCase) ? 730 : 480,
            MinWidth = 430,
            MinHeight = 360,
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
            IsDefault = saveLabel == "Save"
        };
        var cancel = new Button
        {
            Content = "Cancel",
            IsCancel = true
        };
        body.Children.Add(Row(save, cancel));
        dialog.Content = new ScrollViewer { Content = body };
        var saving = false;
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
        save.Click += async (_, _) =>
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
                _historyCursors.Clear();
                _historyOffset = 0;
                dialog.Close();
            }
            catch (Exception ex) { error.Text = FriendlyError(ex); }
            finally { saving = false; body.IsEnabled = true; }
        };
        dialog.Opened += (_, _) =>
        {
            if (fields.FirstOrDefault() is StackPanel panel && panel.Children.LastOrDefault() is InputElement input)
            {
                input.Focus();
            }
        };
        await dialog.ShowDialog(this);
        await Run(Refresh);
    }

    private static Money ParseMoney(TextBox input) => Money.FromFrancs(decimal.Parse(input.Text ?? "", NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite, CultureInfo.InvariantCulture));
    private static Money? OptionalMoney(TextBox input) => string.IsNullOrWhiteSpace(input.Text) ? null : ParseMoney(input);
    private static DatePicker DateInput(DateOnly? date) => new()
    {
        SelectedDate = date?.ToDateTime(TimeOnly.MinValue),
        HorizontalAlignment = HorizontalAlignment.Stretch
    };
    private static DateOnly ParseDate(TextBox input) => DateOnly.ParseExact(input.Text ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static TextBox Input(string text) => new()
    {
        Text = text,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };
    private static TextBlock Text(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        Foreground = Brush.Parse("#344D44")
    };
    private static string AmountText(Money value) => value.Francs.ToString("N2", CultureInfo.GetCultureInfo("de-CH"));
    private static string EntryText(LedgerEntry entry) => $"{entry.Draft.Date:yyyy-MM-dd}  ·  {entry.Draft.Kind}  ·  {AmountText(entry.Draft.Amount)}  ·  {entry.AccountName}{(entry.DestinationName is null ? "" : " → " + entry.DestinationName)}  ·  {entry.Draft.Description}  ·  {entry.CategoryPath ?? ""}";
    private static StackPanel Field(string label, Control input)
    {
        AutomationProperties.SetName(input, label);
        return new()
        {
            Spacing = 5,
            Children = { Text(label), input }
        };
    }
    private static WrapPanel Row(params Control[] controls)
    {
        var row = new WrapPanel();
        foreach (var control in controls)
        {
            control.Margin = new Thickness(0, 0, 10, 10);
            row.Children.Add(control);
        }
        return row;
    }

    private static Button ActionButton(string title, Func<Task> action)
    {
        var button = new Button { Content = title };
        button.Click += async (_, _) => await action();
        return button;
    }
    private sealed record Choice<T>(T Value, string Label)
    {
        public override string ToString() => Label;
    }
}
