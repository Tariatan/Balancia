using Balancia.Storage;

using static Balancia.Desktop.Localization.UiText;

namespace Balancia.Desktop;

/// <summary>Turns a failure into the one-line message shown to the user, without technical detail.</summary>
internal static class FriendlyErrors
{
    internal static string For(Exception ex)
    {
        var english = ex switch
        {
            _ when LedgerFailures.IsConstraintViolation(ex) => "This change conflicts with existing data. Check names and referenced accounts/categories.",
            _ when LedgerFailures.IsDatabaseFailure(ex) => "The database could not be read or saved. Close other Balancia windows and try again.",
            OverflowException => "This amount or resulting total is outside the supported range.",
            FormatException => "Check the date (YYYY-MM-DD) and amount (for example 12.50).",
            IOException or UnauthorizedAccessException => "The local data folder is unavailable or not writable.",
            InvalidDataException or ArgumentException or InvalidOperationException => ex.Message.Split('\n')[0].TrimEnd('\r'),
            _ => "The operation failed. Your entered values have been kept; try again."
        };
        return Get(english);
    }
}
