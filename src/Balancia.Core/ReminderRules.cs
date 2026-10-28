namespace Balancia.Core;

/// <summary>
/// The rules for reminder templates, so that the ledger store, the dialog and the CSV import enforce one definition.
/// Two active reminders may not share a description; the comparison is exact and case-sensitive, as reminder matching is.
/// </summary>
public static class ReminderRules
{
    public static readonly string DescriptionRequiredMessage = "Enter a description.";

    public static readonly string AmountMessage = "Enter amount greater than zero.";

    public static readonly string IntervalMessage = "Repeat interval must be positive.";

    public static readonly string DuplicateDescriptionMessage = "An active reminder already uses this description.";

    public static string? CheckDescription(string description) =>
        string.IsNullOrWhiteSpace(description) ? DescriptionRequiredMessage : null;

    public static string? CheckAmount(Money amount) => amount <= Money.Zero ? AmountMessage : null;

    public static string? CheckInterval(int intervalMonths) => intervalMonths <= 0 ? IntervalMessage : null;

    /// <summary>Whether an active template other than <paramref name="id"/> already has <paramref name="description"/>.</summary>
    public static bool IsDuplicateDescription(IReadOnlyList<ReminderTemplate> activeTemplates, string? id, string description) =>
        activeTemplates.Any(t => t.Id != id && !t.Archived && t.Description == description.Trim());

    /// <summary>
    /// Why saving the reminder cannot go ahead, or null when it can. <paramref name="activeTemplates"/> holds the existing active templates,
    /// including the one being edited; an archived reminder may share a description.
    /// </summary>
    public static string? CheckSave(IReadOnlyList<ReminderTemplate> activeTemplates, string? id, string description, Money amount, int intervalMonths, bool archived)
    {
        var message = CheckDescription(description) ?? CheckAmount(amount) ?? CheckInterval(intervalMonths);
        if (message is not null)
        {
            return message;
        }

        return !archived && IsDuplicateDescription(activeTemplates, id, description) ? DuplicateDescriptionMessage : null;
    }
}
