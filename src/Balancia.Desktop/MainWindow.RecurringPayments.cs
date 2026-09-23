using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Balancia.Core;

namespace Balancia.Desktop;

public partial class MainWindow
{
    private Border RemindersPanel()
    {
        var body = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            RowSpacing = 5
        };

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(0, 0, 0, 7)
        };

        header.Children.Add(Heading("Reminders", 13));

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 3
        };

        var recurring = new ListBox
        {
            ItemsSource = reminders.Select(r => new Choice<RecurringReminder>(r, ReminderText(r))).ToArray(),
            MinHeight = 45,
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
        var remindersContent = new Grid();
        remindersContent.Children.Add(recurring);
        var empty = QuietText("No recurring payment templates.", 12);
        empty.IsVisible = reminders.Count == 0;
        empty.VerticalAlignment = VerticalAlignment.Top;
        empty.Margin = new Thickness(3, 4, 3, 0);
        remindersContent.Children.Add(empty);

        actions.Children.Add(IconButton("+", "Add recurring payment", () => EditRecurring(null)));
        actions.Children.Add(IconButton("🗑", "Delete selected recurring payment", () => DeleteSelectedRecurring(recurring)));

        AddColumn(header, actions, 1);
        AddRow(body, header, 0);

        recurring.DoubleTapped += async (_, _) =>
        {
            if (recurring.SelectedItem is Choice<RecurringReminder> selected)
            {
                await EditRecurring(selected.Value);
            }
        };

        AddRow(body, remindersContent, 1);

        var totalNet = reminders.Aggregate(Money.Zero,
            (total, reminder) => total + reminder.Template.IndicativeAmount);
        var upcomingMonth = displayDate.AddMonths(1);
        var totalUpcomingMonth = reminders
            .Where(reminder => reminder.Occurrence.Year == upcomingMonth.Year && reminder.Occurrence.Month == upcomingMonth.Month)
            .Aggregate(Money.Zero, (total, reminder) => total + reminder.Template.IndicativeAmount);
        var totals = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            ColumnSpacing = 8,
            Margin = new Thickness(0, 5, 0, 0)
        };
        AddColumn(totals, ReminderTotal("TOTAL NET", totalNet), 0);
        AddColumn(totals, ReminderTotal("TOTAL UPCOMING MONTH", totalUpcomingMonth), 1);
        AddRow(body, totals, 2);

        return Panel(body);
    }

    private static StackPanel ReminderTotal(string label, Money amount)
    {
        var total = new StackPanel
        {
            Spacing = 3
        };
        total.Children.Add(QuietText(label, 10));
        total.Children.Add(new TextBlock
        {
            Text = AmountText(amount),
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush.Parse("#263C48")
        });
        return total;
    }

    private async Task DeleteSelectedRecurring(ListBox recurring)
    {
        if (recurring.SelectedItem is not Choice<RecurringReminder> choice)
        {
            await SelectFirst();
            return;
        }

        await EditDialog("Delete recurring payment",
            [
                Text($"Delete '{choice.Value.Template.Description}'?")
            ],
            () => () => store.DeleteRecurringTemplate(choice.Value.Template.Id),
            "Delete");
    }

    private static string ReminderText(RecurringReminder reminder) =>
        $"{(reminder.Overdue ? "OVERDUE · " : "")}{reminder.Occurrence:yyyy-MM-dd} · {reminder.Template.Description} · {AmountText(reminder.Template.IndicativeAmount)} · every {reminder.Template.IntervalMonths} month(s)";

    private async Task EditRecurring(RecurringReminder? reminder)
    {
        var template = reminder?.Template;
        var description = Input(template?.Description ?? "");
        var date = DateInput(template?.ExpectedDate ?? displayDate);
        var amount = Input((template?.IndicativeAmount.Francs ?? 0).ToString("0.00", CultureInfo.InvariantCulture));
        var interval = Input((template?.IntervalMonths ?? 1).ToString(CultureInfo.InvariantCulture));

        await EditDialog(template is null ? "Add recurring template" : "Edit recurring template",
            [
                Field("Description (exact match)", description),
                Field("Expected date", date),
                Field("Amount", amount),
                Field("Repeat every N months", interval)
            ],
            () =>
            {
                var values = (description.Text ?? "", ParseDate(date), ParseMoney(amount),
                    int.Parse(interval.Text ?? "", CultureInfo.InvariantCulture));
                return () => store.SaveRecurringTemplate(template?.Id, values.Item1, values.Item2, values.Item3, values.Item4, template?.Archived ?? false);
            });
    }
}
