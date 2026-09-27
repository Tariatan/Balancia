using System.Globalization;
using System.Resources;
using System.Threading;

namespace Balancia.Desktop.Localization;

internal static class UiText
{
    private static readonly ResourceManager Resources = new("Balancia.Desktop.Localization.Strings", typeof(UiText).Assembly);
    private static CultureInfo resourceCulture = CultureInfo.GetCultureInfo("en-US");

    internal static string Language { get; private set; } = "en";
    internal static CultureInfo Culture => Volatile.Read(ref resourceCulture);

    internal static string ResolveLanguage(string? savedLanguage, CultureInfo operatingSystemCulture)
    {
        var language = savedLanguage ?? operatingSystemCulture.TwoLetterISOLanguageName;
        return language is "en" or "de" or "ru" or "uk" ? language : "en";
    }

    internal static void SetLanguage(string language)
    {
        Language = ResolveLanguage(language, CultureInfo.GetCultureInfo("en-US"));
        var culture = CultureInfo.GetCultureInfo(Language switch
        {
            "de" => "de-DE",
            "ru" => "ru-RU",
            "uk" => "uk-UA",
            _ => "en-US",
        });
        Volatile.Write(ref resourceCulture, culture);
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
    }

    internal static string Get(string english)
    {
        var culture = Volatile.Read(ref resourceCulture);
        return Resources.GetString(english, culture) ?? english;
    }

    internal static string Format(string english, params object?[] args)
    {
        var culture = Volatile.Read(ref resourceCulture);
        var format = Resources.GetString(english, culture) ?? english;
        return string.Format(culture, format, args);
    }
}
