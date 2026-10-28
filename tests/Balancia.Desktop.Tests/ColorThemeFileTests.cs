using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Xunit;

namespace Balancia.Desktop.Tests;

public sealed class ColorThemeFileTests
{
    [AvaloniaFact]
    public void Parse_ValidFile_ReturnsTheNameAndTheColors()
    {
        // Act
        var result = ColorThemeFile.Parse("""{ "name": "Dusk", "colors": { "Text": "#E0E0E0", "Accent": "#102030" } }""", "dusk.json");

        // Assert
        Assert.Empty(result.Warnings);
        Assert.Equal("Dusk", result.Theme!.Name);
        Assert.Equal(Color.Parse("#E0E0E0"), result.Theme.Colors["Text"]);
        Assert.Equal(Color.Parse("#102030"), result.Theme.Colors["Accent"]);
        Assert.Equal(2, result.Theme.Colors.Count);
    }

    [AvaloniaTheory]
    [InlineData("""{ "colors": { "Text": "#E0E0E0" } }""")]
    [InlineData("""{ "name": "   ", "colors": { "Text": "#E0E0E0" } }""")]
    [InlineData("""{ "name": 5, "colors": { "Text": "#E0E0E0" } }""")]
    public void Parse_NoUsableName_UsesTheFileNameWithoutItsExtension(string json)
    {
        // Act
        var result = ColorThemeFile.Parse(json, "night-owl.json");

        // Assert
        Assert.Equal("night-owl", result.Theme!.Name);
    }

    [AvaloniaFact]
    public void Parse_RoleNamesIgnoreLetterCase_AndAreStoredWithTheirRealName()
    {
        // Act
        var result = ColorThemeFile.Parse("""{ "colors": { "textmuted": "#111111", "ACCENT": "#222222" } }""", "t.json");

        // Assert
        Assert.Empty(result.Warnings);
        Assert.Equal(["Accent", "TextMuted"], result.Theme!.Colors.Keys.Order());
    }

    [AvaloniaFact]
    public void Parse_ColorWithAlpha_KeepsTheAlpha()
    {
        // Act
        var result = ColorThemeFile.Parse("""{ "colors": { "AccentTintStrong": "#80112233" } }""", "t.json");

        // Assert
        Assert.Equal(Color.FromArgb(0x80, 0x11, 0x22, 0x33), result.Theme!.Colors["AccentTintStrong"]);
    }

    [AvaloniaFact]
    public void Parse_UnknownRole_IsIgnoredWithAWarning()
    {
        // Act
        var result = ColorThemeFile.Parse("""{ "colors": { "Text": "#E0E0E0", "Sparkle": "#FFFFFF" } }""", "t.json");

        // Assert
        Assert.Equal(["Text"], result.Theme!.Colors.Keys);
        Assert.Equal(["Unknown color role 'Sparkle' ignored."], result.Warnings);
    }

    [AvaloniaTheory]
    [InlineData("\"red-ish\"")]
    [InlineData("\"#12345\"")]
    [InlineData("\"\"")]
    [InlineData("42")]
    [InlineData("null")]
    public void Parse_UnreadableColor_KeepsTheBuiltInColorWithAWarning(string value)
    {
        // Act
        var result = ColorThemeFile.Parse($$"""{ "colors": { "Text": {{value}}, "Accent": "#102030" } }""", "t.json");

        // Assert
        Assert.Equal(["Accent"], result.Theme!.Colors.Keys);
        Assert.Equal(["The color of 'Text' is not #RRGGBB or #AARRGGBB; the built-in color is kept."], result.Warnings);
    }

    [AvaloniaTheory]
    [InlineData("not json at all")]
    [InlineData("")]
    [InlineData("""{ "colors": """)]
    public void Parse_InvalidJson_IsRefusedWithAWarning(string json)
    {
        // Act
        var result = ColorThemeFile.Parse(json, "t.json");

        // Assert
        Assert.Null(result.Theme);
        Assert.StartsWith("The file is not valid JSON", Assert.Single(result.Warnings));
    }

    [AvaloniaTheory]
    [InlineData("[1, 2]")]
    [InlineData("""{ "name": "No colors" }""")]
    [InlineData("""{ "colors": [] }""")]
    [InlineData("""{ "colors": "#FFFFFF" }""")]
    public void Parse_NotAnObjectWithColors_IsRefusedWithAWarning(string json)
    {
        // Act
        var result = ColorThemeFile.Parse(json, "t.json");

        // Assert
        Assert.Null(result.Theme);
        Assert.Equal(["The file must be a JSON object with a \"colors\" object."], result.Warnings);
    }

    [AvaloniaFact]
    public void Parse_CommentsAndTrailingCommas_AreAllowed()
    {
        // Act
        var result = ColorThemeFile.Parse("""
            {
              // a theme written by hand
              "colors": { "Text": "#E0E0E0", },
            }
            """, "t.json");

        // Assert
        Assert.Empty(result.Warnings);
        Assert.Equal(["Text"], result.Theme!.Colors.Keys);
    }

    [AvaloniaFact]
    public void Parse_EmptyColors_IsAThemeThatChangesNothing()
    {
        // Act
        var result = ColorThemeFile.Parse("""{ "name": "Plain", "colors": {} }""", "t.json");

        // Assert
        Assert.Empty(result.Theme!.Colors);
    }
}

public sealed class ColorThemeCatalogTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "balancia-themes-" + Guid.NewGuid().ToString("N"));

    public ColorThemeCatalogTests() => Directory.CreateDirectory(directory);

    public void Dispose() => Directory.Delete(directory, true);

    private void Write(string fileName, string json) => File.WriteAllText(Path.Combine(directory, fileName), json);

    [AvaloniaFact]
    public void Load_ThemeFiles_ComeInFileNameOrder()
    {
        // Arrange
        Write("b-second.json", """{ "name": "Second", "colors": { "Text": "#222222" } }""");
        Write("a-first.json", """{ "name": "First", "colors": { "Text": "#111111" } }""");

        // Act
        var themes = ColorThemeCatalog.Load(directory);

        // Assert
        Assert.Equal(["First", "Second"], themes.Select(theme => theme.Name));
    }

    [AvaloniaFact]
    public void Load_NoSuchFolderOrNoFolder_ReturnsNothing()
    {
        // Act and Assert
        Assert.Empty(ColorThemeCatalog.Load(Path.Combine(directory, "missing")));
        Assert.Empty(ColorThemeCatalog.Load(null));
    }

    [AvaloniaFact]
    public void Load_UnusableFiles_AreSkippedAndTheRestStillLoads()
    {
        // Arrange
        Write("a-broken.json", "{ nope");
        Write("b-no-colors.json", """{ "name": "Empty" }""");
        Write("c-good.json", """{ "name": "Good", "colors": { "Text": "#111111" } }""");
        Write("d-notes.txt", """{ "name": "Not a theme file", "colors": {} }""");

        // Act
        var themes = ColorThemeCatalog.Load(directory);

        // Assert
        Assert.Equal(["Good"], themes.Select(theme => theme.Name));
    }

    [AvaloniaFact]
    public void Load_TheBuiltInName_IsRefusedWhateverItsLetterCase()
    {
        // Arrange
        Write("a.json", """{ "name": "balancia", "colors": { "Text": "#111111" } }""");

        // Act and Assert
        Assert.Empty(ColorThemeCatalog.Load(directory));
    }

    [AvaloniaFact]
    public void Load_TwoFilesWithOneName_KeepsTheFirst()
    {
        // Arrange
        Write("a.json", """{ "name": "Dusk", "colors": { "Text": "#111111" } }""");
        Write("b.json", """{ "name": "DUSK", "colors": { "Text": "#222222" } }""");

        // Act
        var theme = Assert.Single(ColorThemeCatalog.Load(directory));

        // Assert
        Assert.Equal(Color.Parse("#111111"), theme.Colors["Text"]);
    }

    [AvaloniaFact]
    public void Load_FileWithAMistake_StillChangesWhatItCan()
    {
        // Arrange
        Write("a.json", """{ "name": "Mostly", "colors": { "Text": "#111111", "Sparkle": "#222222", "Accent": "oops" } }""");

        // Act
        var theme = Assert.Single(ColorThemeCatalog.Load(directory));

        // Assert
        Assert.Equal(["Text"], theme.Colors.Keys);
    }
}
