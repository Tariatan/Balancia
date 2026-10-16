using System.Globalization;
using System.Resources;
using Balancia.Desktop.Localization;
using Xunit;

namespace Balancia.Desktop.Tests;

public sealed class LocalizationTests
{
    private static HashSet<string> Keys(CultureInfo culture)
    {
        // The resource manager caches these sets for the whole process; disposing one would break every later lookup.
        var set = UiText.Resources.GetResourceSet(culture, createIfNotExists: true, tryParents: false)
            ?? throw new InvalidOperationException($"No resources for '{culture.Name}'.");
        return [.. set.Cast<System.Collections.DictionaryEntry>().Select(entry => (string)entry.Key)];
    }

    [Theory]
    [InlineData("de")]
    [InlineData("ru")]
    [InlineData("uk")]
    public void Resources_TranslatedLanguage_HasExactlyTheEnglishKeys(string language)
    {
        // Arrange
        var english = Keys(CultureInfo.InvariantCulture);

        // Act
        var translated = Keys(CultureInfo.GetCultureInfo(language));

        // Assert
        Assert.Empty(english.Except(translated));
        Assert.Empty(translated.Except(english));
    }

    [Theory]
    [InlineData("de")]
    [InlineData("ru")]
    [InlineData("uk")]
    public void Resources_TranslatedLanguage_HasNoEmptyValueAndKeepsFormatPlaceholders(string language)
    {
        // Arrange
        var culture = CultureInfo.GetCultureInfo(language);

        // Act
        var english = UiText.Resources.GetResourceSet(CultureInfo.InvariantCulture, true, false)!;
        var translated = UiText.Resources.GetResourceSet(culture, true, false)!;

        // Assert
        foreach (var entry in english.Cast<System.Collections.DictionaryEntry>())
        {
            var key = (string)entry.Key;
            var value = translated.GetString(key);
            Assert.False(string.IsNullOrWhiteSpace(value), $"'{key}' has no {language} text.");
            var placeholders = System.Text.RegularExpressions.Regex.Matches(key, @"\{\d+\}").Select(match => match.Value).Order();
            Assert.Equal(placeholders, System.Text.RegularExpressions.Regex.Matches(value!, @"\{\d+\}").Select(match => match.Value).Order());
        }
    }
}
