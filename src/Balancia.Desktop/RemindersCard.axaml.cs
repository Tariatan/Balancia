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

/// <summary>The reminders card: upcoming and overdue payments with their totals. It draws what it is given and raises requests; dialogs and saving stay with the window.</summary>
public partial class RemindersCard : UserControl
{
    private DateOnly today;

    public RemindersCard()
    {
        InitializeComponent();
        RemindersList.Styles.Add(new Style(selector => selector.OfType<ListBoxItem>())
        {
            Setters =
            {
                new Setter(BackgroundProperty, new Binding(".")
                {
                    Converter = new FuncValueConverter<Choice<Reminder>, IBrush>(choice =>
                        choice is not null && ReminderPresentation.IsBeyondNextMonth(choice.Value, today)
                            ? RowBrushes.Blue : RowBrushes.Ivory),
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
            var emphasize = ReminderPresentation.IsEmphasized(reminder, today);
            var foreground = Brush.Parse(ReminderPresentation.ColorFor(reminder, today));

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
            GridLayout.Place(row, Cell(reminder.Occurrence.ToString("dd MMM", Culture), FontWeight.Normal), 0);
            GridLayout.Place(row, description, 1);
            GridLayout.Place(row, amount, 2);
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
        RemindersList.DoubleTapped += (_, _) =>
        {
            if (Selected is { } reminder)
            {
                EditRequested?.Invoke(reminder);
            }
        };
        ReminderAddButton.Click += (_, _) => AddRequested?.Invoke();
        ReminderRemoveButton.Click += (_, _) => RemoveRequested?.Invoke();
    }

    /// <summary>The add button was pressed.</summary>
    public event Action? AddRequested;

    /// <summary>The remove button was pressed; <see cref="Selected"/> says what, if anything, it refers to.</summary>
    public event Action? RemoveRequested;

    /// <summary>A row was double-tapped.</summary>
    public event Action<Reminder>? EditRequested;

    public Reminder? Selected => (RemindersList.SelectedItem as Choice<Reminder>)?.Value;

    /// <summary>Shows <paramref name="reminders"/> as of <paramref name="today"/>, with the current language.</summary>
    public void Render(IReadOnlyList<Reminder> reminders, DateOnly today)
    {
        this.today = today;
        RemindersHeading.Text = Get("Reminders");
        ToolTip.SetTip(ReminderAddButton, Get("Add reminder"));
        ToolTip.SetTip(ReminderRemoveButton, Get("Delete selected reminder"));

        RemindersList.ItemsSource = reminders.Select(r => new Choice<Reminder>(r, r.Template.Description)).ToArray();
        RemindersEmpty.Text = Get("No reminders yet");
        RemindersEmpty.IsVisible = reminders.Count == 0;

        var totals = ReminderPresentation.Summarize(reminders, today);
        ReminderTotalNetLabel.Text = Get("TOTAL");
        ReminderTotalNetValue.Text = AmountText(totals.Net);
        ReminderTotalUpcomingLabel.Text = Get(totals.UpcomingIsThisMonth ? "UPCOMING THIS MONTH" : "UPCOMING NEXT MONTH");
        ReminderTotalUpcomingValue.Text = AmountText(totals.Upcoming);
    }
}
