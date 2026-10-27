using static Balancia.Desktop.Localization.UiText;

namespace Balancia.Desktop;

internal static class ArchivedLabel
{
    /// <summary>The text with the localized "(archived)" marker appended when <paramref name="archived"/> is set.</summary>
    internal static string Of(string text, bool archived) => archived ? text + Get(" (archived)") : text;
}
