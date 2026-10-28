using System.Globalization;
using Balancia.Core;

using static Balancia.Desktop.FormControls;
using static Balancia.Desktop.InputParsing;
using static Balancia.Desktop.Localization.UiText;

namespace Balancia.Desktop;

public partial class MainWindow
{
    private void InitializeRemindersPanel()
    {
        OverviewPane.RemindersCard.AddRequested += async () => await EditReminder(null);
        OverviewPane.RemindersCard.RemoveRequested += async () => await DeleteSelectedReminder();
        OverviewPane.RemindersCard.EditRequested += async reminder => await EditReminder(reminder);
    }

    private async Task DeleteSelectedReminder()
    {
        if (OverviewPane.RemindersCard.Selected is not { } reminder)
        {
            await SelectFirst();
            return;
        }

        await dialogs.ShowAsync("Delete reminder",
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
        var date = DateInput(template?.ExpectedDate ?? OverviewPane.DisplayDate);
        var amount = Input((template?.IndicativeAmount.Francs ?? 0).ToString("0.00", CultureInfo.InvariantCulture));
        var interval = Input((template?.IntervalMonths ?? 1).ToString(CultureInfo.InvariantCulture));

        await dialogs.ShowAsync(template is null ? "Add reminder" : "Edit reminder",
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
            new DialogOptions(Validate: () => ReminderRules.CheckSave(
                [.. OverviewPane.Data!.Reminders.Select(r => r.Template)], template?.Id, description.Text ?? string.Empty, ParseMoney(amount),
                int.TryParse(interval.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedInterval) ? parsedInterval : 0,
                template?.Archived ?? false)));
    }
}
