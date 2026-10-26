using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Balancia.Core;

using static Balancia.Desktop.InputParsing;
using static Balancia.Desktop.Localization.UiText;

namespace Balancia.Desktop;

public partial class MainWindow
{
    private Reminder? SelectedReminder => (RemindersList.SelectedItem as Choice<Reminder>)?.Value;

    private void InitializeRemindersPanel()
    {
        RemindersList.Styles.Add(new Style(selector => selector.OfType<ListBoxItem>())
        {
            Setters =
            {
                new Setter(BackgroundProperty, new Binding(".")
                {
                    Converter = new FuncValueConverter<Choice<Reminder>, IBrush>(choice =>
                        choice is not null && ReminderPresentation.IsBeyondNextMonth(choice.Value, displayDate)
                            ? DayBlueBrush : DayIvoryBrush),
                }),
            },
        });
        RemindersList.ItemTemplate = new FuncDataTemplate<Choice<Reminder>>((choice, _) =>
        {
            // Virtualized presenters can request a template with no item while scrolling.
            if (choice is null)
            {
                return null;
            }

            var reminder = choice.Value;
            var emphasize = ReminderPresentation.IsEmphasized(reminder, displayDate);
            var foreground = Brush.Parse(ReminderPresentation.ColorFor(reminder, displayDate));

            var description = Cell(reminder.Template.Description, FontWeight.Normal);
            description.TextTrimming = TextTrimming.CharacterEllipsis;

            var amount = Cell(AmountText(reminder.Template.IndicativeAmount), FontWeight.SemiBold);
            amount.HorizontalAlignment = HorizontalAlignment.Right;

            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("50,*,Auto"),
                ColumnSpacing = 10,
                MinHeight = 20,
            };
            AddColumn(row, Cell(reminder.Occurrence.ToString("dd MMM", Culture), FontWeight.Normal), 0);
            AddColumn(row, description, 1);
            AddColumn(row, amount, 2);
            return row;

            TextBlock Cell(string text, FontWeight weight) => new()
            {
                Text = text,
                FontSize = 12,
                FontWeight = emphasize ? FontWeight.Bold : weight,
                Foreground = foreground,
                VerticalAlignment = VerticalAlignment.Center,
            };
        },
        true);
        RemindersList.DoubleTapped += async (_, _) =>
        {
            if (SelectedReminder is { } reminder)
            {
                await EditReminder(reminder);
            }
        };
        ReminderAddButton.Click += async (_, _) => await EditReminder(null);
        ReminderRemoveButton.Click += async (_, _) => await DeleteSelectedReminder();
    }

    private void RenderRemindersPanel()
    {
        RemindersHeading.Text = Get("Reminders");
        ToolTip.SetTip(ReminderAddButton, Get("Add reminder"));
        ToolTip.SetTip(ReminderRemoveButton, Get("Delete selected reminder"));

        RemindersList.ItemsSource = reminders.Select(r => new Choice<Reminder>(r, r.Template.Description)).ToArray();
        RemindersEmpty.Text = Get("No reminders yet");
        RemindersEmpty.IsVisible = reminders.Count == 0;

        var totals = ReminderPresentation.Summarize(reminders, displayDate);
        ReminderTotalNetLabel.Text = Get("TOTAL");
        ReminderTotalNetValue.Text = AmountText(totals.Net);
        ReminderTotalUpcomingLabel.Text = Get(totals.UpcomingIsThisMonth ? "UPCOMING THIS MONTH" : "UPCOMING NEXT MONTH");
        ReminderTotalUpcomingValue.Text = AmountText(totals.Upcoming);
    }

    private async Task DeleteSelectedReminder()
    {
        if (SelectedReminder is not { } reminder)
        {
            await SelectFirst();
            return;
        }

        await EditDialog("Delete reminder",
            [
                Text(Format("Delete '{0}'?", reminder.Template.Description))
            ],
            () => () => store.DeleteReminder(reminder.Template.Id),
            DialogOptions.Confirmation("Delete"));
    }

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
                var (text, expectedDate, indicativeAmount, intervalMonths) = (description.Text ?? "", ParseDate(date), ParseMoney(amount),
                    int.Parse(interval.Text ?? "", CultureInfo.InvariantCulture));
                return () => store.SaveReminder(template?.Id, text, expectedDate, indicativeAmount, intervalMonths, template?.Archived ?? false);
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
