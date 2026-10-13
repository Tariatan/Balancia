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
        RemindersList.ItemTemplate = new FuncDataTemplate<Choice<Reminder>>((choice, _) =>
        {
            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("50,*,Auto"),
                ColumnSpacing = 10,
                MinHeight = 20
            };
            var reminder = choice.Value;
            var emphasize = ReminderPresentation.IsEmphasized(reminder, displayDate);
            var foreground = Brush.Parse(ReminderPresentation.ColorFor(reminder, displayDate));

            AddColumn(row, new TextBlock
            {
                Text = reminder.Occurrence.ToString("dd MMM", Culture),
                FontSize = 12,
                FontWeight = emphasize ? FontWeight.Bold : FontWeight.Normal,
                Foreground = foreground,
                VerticalAlignment = VerticalAlignment.Center
            }, 0);
            AddColumn(row, new TextBlock
            {
                Text = reminder.Template.Description,
                FontSize = 12,
                FontWeight = emphasize ? FontWeight.Bold : FontWeight.Normal,
                Foreground = foreground,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            }, 1);

            var amount = new TextBlock
            {
                Text = AmountText(reminder.Template.IndicativeAmount),
                FontSize = 12,
                Foreground = foreground,
                FontWeight = emphasize ? FontWeight.Bold : FontWeight.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };

            AddColumn(row, amount, 2);
            return row;
        },
        true);
        RemindersList.DoubleTapped += async (_, _) =>
        {
            if (RemindersList.SelectedItem is Choice<Reminder> selected)
            {
                await EditReminder(selected.Value);
            }
        };
        ReminderAddButton.Click += async (_, _) => await EditReminder(null);
        ReminderRemoveButton.Click += async (_, _) => await DeleteSelectedReminder(RemindersList);
    }

    private void RenderRemindersPanel()
    {
        RemindersHeading.Text = Get("Reminders");
        ToolTip.SetTip(ReminderAddButton, Get("Add reminder"));
        ToolTip.SetTip(ReminderRemoveButton, Get("Delete selected reminder"));

        RemindersList.ItemsSource = reminders.Select(r => new Choice<Reminder>(r, ReminderText(r))).ToArray();
        RemindersEmpty.Text = Get("No reminders yet");
        RemindersEmpty.IsVisible = reminders.Count == 0;

        var totals = ReminderPresentation.Summarize(reminders, displayDate);
        ReminderTotalNetLabel.Text = Get("TOTAL");
        ReminderTotalNetValue.Text = AmountText(totals.Net);
        ReminderTotalUpcomingLabel.Text = Get(totals.UpcomingIsThisMonth ? "UPCOMING THIS MONTH" : "UPCOMING NEXT MONTH");
        ReminderTotalUpcomingValue.Text = AmountText(totals.Upcoming);
    }

    private async Task DeleteSelectedReminder(ListBox remindersList)
    {
        if (remindersList.SelectedItem is not Choice<Reminder> choice)
        {
            await SelectFirst();
            return;
        }

        await EditDialog("Delete reminder",
            [
                Text(Format("Delete '{0}'?", choice.Value.Template.Description))
            ],
            () => () => store.DeleteReminder(choice.Value.Template.Id),
            DialogOptions.Confirmation("Delete"));
    }

    private static string ReminderText(Reminder reminder) =>
        $"{(reminder.Overdue ? Get("OVERDUE · ") : "")}{reminder.Occurrence:yyyy-MM-dd} · {reminder.Template.Description} · {AmountText(reminder.Template.IndicativeAmount)} · {Format("every {0} month(s)", reminder.Template.IntervalMonths)}";

    private async Task EditReminder(Reminder? reminder)
    {
        var template = reminder?.Template;
        var description = Input(template?.Description ?? "");
        var date = DateInput(template?.ExpectedDate ?? displayDate);
        var amount = Input((template?.IndicativeAmount.Francs ?? 0).ToString("0.00", CultureInfo.InvariantCulture));
        var interval = Input((template?.IntervalMonths ?? 1).ToString(CultureInfo.InvariantCulture));

        await EditDialog(template is null ? "Add reminder" : "Edit reminder",
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
                return () => store.SaveReminder(template?.Id, values.Item1, values.Item2, values.Item3, values.Item4, template?.Archived ?? false);
            },
            new DialogOptions(Validate: () =>
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
            }));
    }
}
