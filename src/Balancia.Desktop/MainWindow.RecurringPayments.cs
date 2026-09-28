using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Balancia.Core;

using static Balancia.Desktop.Localization.UiText;

namespace Balancia.Desktop;

public partial class MainWindow
{
    private void InitializeRemindersPanel()
    {
        RemindersList.ItemTemplate = new FuncDataTemplate<Choice<RecurringReminder>>((choice, _) =>
        {
            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("50,*,Auto"),
                ColumnSpacing = 10,
                MinHeight = 20
            };
            var reminder = choice.Value;

            AddColumn(row, new TextBlock
            {
                Text = reminder.Occurrence.ToString("dd MMM", Culture),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            }, 0);
            AddColumn(row, new TextBlock
            {
                Text = reminder.Template.Description,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            }, 1);

            var amount = new TextBlock
            {
                Text = AmountText(reminder.Template.IndicativeAmount),
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };

            AddColumn(row, amount, 2);
            return row;
        },
        true);
        RemindersList.DoubleTapped += async (_, _) =>
        {
            if (RemindersList.SelectedItem is Choice<RecurringReminder> selected)
            {
                await EditRecurring(selected.Value);
            }
        };
        overviewReminderAddButton.Click += async (_, _) => await EditRecurring(null);
        overviewReminderRemoveButton.Click += async (_, _) => await DeleteSelectedRecurring(RemindersList);
    }

    private void RenderRemindersPanel()
    {
        overviewRemindersHeading.Text = Get("Reminders");
        ToolTip.SetTip(overviewReminderAddButton, Get("Add recurring payment"));
        ToolTip.SetTip(overviewReminderRemoveButton, Get("Delete selected recurring payment"));

        RemindersList.ItemsSource = reminders.Select(r => new Choice<RecurringReminder>(r, ReminderText(r))).ToArray();
        overviewRemindersEmpty.Text = Get("No recurring payment templates.");
        overviewRemindersEmpty.IsVisible = reminders.Count == 0;

        var totalNet = reminders.Aggregate(Money.Zero,
            (total, reminder) => total + reminder.Template.IndicativeAmount);
        var upcomingMonth = displayDate.AddMonths(1);
        var totalUpcomingMonth = reminders
            .Where(reminder => reminder.Occurrence.Year == upcomingMonth.Year && reminder.Occurrence.Month == upcomingMonth.Month)
            .Aggregate(Money.Zero, (total, reminder) => total + reminder.Template.IndicativeAmount);
        overviewReminderTotalNetLabel.Text = Get("TOTAL NET");
        overviewReminderTotalNetValue.Text = AmountText(totalNet);
        overviewReminderTotalUpcomingLabel.Text = Get("TOTAL UPCOMING MONTH");
        overviewReminderTotalUpcomingValue.Text = AmountText(totalUpcomingMonth);
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
                Text(Format("Delete '{0}'?", choice.Value.Template.Description))
            ],
            () => () => store.DeleteRecurringTemplate(choice.Value.Template.Id),
            "Delete");
    }

    private static string ReminderText(RecurringReminder reminder) =>
        $"{(reminder.Overdue ? Get("OVERDUE · ") : "")}{reminder.Occurrence:yyyy-MM-dd} · {reminder.Template.Description} · {AmountText(reminder.Template.IndicativeAmount)} · {Format("every {0} month(s)", reminder.Template.IntervalMonths)}";

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
            },
            validate: () =>
            {
                if (string.IsNullOrWhiteSpace(description.Text))
                {
                    return "Enter a description.";
                }

                if (ParseMoney(amount) <= Money.Zero)
                {
                    return "Enter amount greater than zero.";
                }

                if (!int.TryParse(interval.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedInterval) || parsedInterval <= 0)
                {
                    return "Repeat interval must be positive.";
                }

                return null;
            });
    }
}
