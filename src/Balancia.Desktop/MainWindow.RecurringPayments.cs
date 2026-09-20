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
    private Border RemindersPanel()
    {
        var body = new StackPanel
        {
            Spacing = 1
        };

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(0, 0, 0, 7)
        };

        header.Children.Add(Heading("Upcoming payments", 13));

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 3
        };

        var recurring = new ListBox
        {
            ItemsSource = _reminders.Take(5).Select(r => new Choice<RecurringReminder>(r, ReminderText(r))).ToArray(),
            MinHeight = _reminders.Count == 0 ? 0 : 45,
            MaxHeight = 175,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            ItemTemplate = new FuncDataTemplate<Choice<RecurringReminder>>((choice, _) =>
            {
                var row = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                    MinHeight = 20
                };
                var reminder = choice.Value;

                row.Children.Add(new TextBlock
                {
                    Text = $"{reminder.Occurrence:dd MMM} · {reminder.Template.Description}",
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });

                var amount = new TextBlock
                {
                    Text = AmountText(reminder.Template.IndicativeAmount),
                    FontSize = 12,
                    FontWeight = FontWeight.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center
                };

                AddColumn(row, amount, 1);
                return row;
            },
            true)
        };

        var add = ActionButton("+", () => EditRecurring(null));
        add.Width = 30;
        add.Padding = new Thickness(0);
        add.FontSize = 18;
        add.HorizontalContentAlignment = HorizontalAlignment.Center;

        var remove = ActionButton("🗑", () => DeleteSelectedRecurring(recurring));
        remove.Width = 30;
        remove.Padding = new Thickness(0);
        remove.FontSize = 18;
        remove.HorizontalContentAlignment = HorizontalAlignment.Center;

        ToolTip.SetTip(add, "Add recurring payment");
        ToolTip.SetTip(remove, "Delete selected recurring payment");

        actions.Children.Add(add);
        actions.Children.Add(remove);
        AddColumn(header, actions, 1);
        body.Children.Add(header);

        recurring.DoubleTapped += async (_, _) =>
        {
            if (recurring.SelectedItem is Choice<RecurringReminder> selected)
            {
                await EditRecurring(selected.Value);
            }
        };

        if (_reminders.Count == 0)
        {
            body.Children.Add(QuietText("No recurring payment templates.", 12));
        }
        else
        {
            body.Children.Add(recurring);
        }

        return Panel(body);
    }

    private async Task DeleteSelectedRecurring(ListBox recurring)
    {
        if (recurring.SelectedItem is not Choice<RecurringReminder> choice)
        {
            await SelectFirst();
            return;
        }
        var dialog = new Window
        {
            Title = "Delete recurring payment",
            Width = 430,
            Height = 210,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var cancel = new Button
        {
            Content = "Cancel",
            IsCancel = true
        };
        var remove = new Button
        {
            Content = "Delete",
            IsDefault = true
        };
        remove.Click += async (_, _) =>
        {
            remove.IsEnabled = false;
            try
            {
                await Task.Run(() => _store.DeleteRecurringTemplate(choice.Value.Template.Id));
                dialog.Close();
                await Run(Refresh);
            }
            catch (Exception ex)
            {
                await ShowErrorDialog("Balancia", FriendlyError(ex));
                remove.IsEnabled = true;
            }
        };
        cancel.Click += (_, _) => dialog.Close();
        dialog.Content = new StackPanel
        {
            Spacing = 14,
            Margin = new Thickness(22),
            Children =
            {
                Text($"Delete '{choice.Value.Template.Description}'?"),
                Row(remove, cancel)
            }
        };
        await dialog.ShowDialog(this);
    }

    private static string ReminderText(RecurringReminder reminder) =>
        $"{(reminder.Overdue ? "OVERDUE · " : "")}{reminder.Occurrence:yyyy-MM-dd} · {reminder.Template.Description} · indicative {AmountText(reminder.Template.IndicativeAmount)} · every {reminder.Template.IntervalMonths} month(s)";

    private async Task EditRecurring(RecurringReminder? reminder)
    {
        var template = reminder?.Template;
        var description = Input(template?.Description ?? "");
        var date = Input((template?.ExpectedDate ?? _displayDate).ToString("yyyy-MM-dd"));
        var amount = Input((template?.IndicativeAmount.Francs ?? 0).ToString("0.00", CultureInfo.InvariantCulture));
        var interval = Input((template?.IntervalMonths ?? 1).ToString(CultureInfo.InvariantCulture));
        await EditDialog(template is null ? "Add recurring template" : "Edit recurring template",
            [Field("Description (exact match)", description), Field("Expected date (YYYY-MM-DD)", date), Field("Indicative amount", amount), Field("Repeat every N months", interval)],
            () =>
            {
                var values = (description.Text ?? "", ParseDate(date), ParseMoney(amount),
                    int.Parse(interval.Text ?? "", CultureInfo.InvariantCulture));
                return () => _store.SaveRecurringTemplate(template?.Id, values.Item1, values.Item2, values.Item3, values.Item4, template?.Archived ?? false);
            });
    }
}
