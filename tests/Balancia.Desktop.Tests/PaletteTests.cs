using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Xunit;

namespace Balancia.Desktop.Tests;

public sealed partial class PaletteTests
{
    private static string DesktopSourceDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Balancia.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(directory.FullName, "src", "Balancia.Desktop");
    }

    private static IEnumerable<string> SourceFiles(string pattern) =>
        Directory.EnumerateFiles(DesktopSourceDirectory(), pattern, SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    [GeneratedRegex(@"#[0-9A-Fa-f]{6}([0-9A-Fa-f]{2})?\b")]
    private static partial Regex HexColor();

    [GeneratedRegex(@"\b(Brush|Color)\.Parse\(")]
    private static partial Regex ColorParsing();

    [GeneratedRegex(@"\b(Foreground|Background|BorderBrush|SelectionBrush|SelectionForegroundBrush|Fill|Stroke)=""(?!\{)(?!Transparent"")[^""]*""")]
    private static partial Regex NamedColorAttribute();

    [GeneratedRegex(@"DynamicResource Palette\.(\w+)")]
    private static partial Regex PaletteKey();

    [AvaloniaFact]
    public void Register_EveryRole_IsAnApplicationResourceHoldingTheSharedBrush()
    {
        // Act and Assert
        foreach (var (name, brush) in Palette.Tokens)
        {
            Assert.True(Application.Current!.Resources.TryGetResource(Palette.ResourceKey(name), null, out var value), name);
            Assert.Same(brush, value);
        }
    }

    [AvaloniaFact]
    public void Tokens_AreNamedAfterTheirFieldsAndEveryFieldIsOne()
    {
        // Act
        var fields = typeof(Palette).GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            .Where(field => field.FieldType == typeof(Avalonia.Media.SolidColorBrush))
            .ToDictionary(field => field.Name, field => field.GetValue(null));

        // Assert
        Assert.Equal(fields.Keys.Order(), Palette.Tokens.Keys.Order());
        Assert.All(Palette.Tokens, token => Assert.Same(fields[token.Key], token.Value));
    }

    [AvaloniaFact]
    public void Xaml_EveryPaletteKeyItUses_IsARole()
    {
        // Act
        var used = SourceFiles("*.axaml").SelectMany(file => PaletteKey().Matches(File.ReadAllText(file)).Select(match => (file, key: match.Groups[1].Value)));

        // Assert
        Assert.All(used, item => Assert.True(Palette.Tokens.ContainsKey(item.key), $"{Path.GetFileName(item.file)} uses Palette.{item.key}, which is not a role."));
    }

    [AvaloniaFact]
    public void Source_HasNoColorLiteralsOutsideThePalette()
    {
        // Act
        var offenders = new List<string>();
        foreach (var file in SourceFiles("*.cs").Concat(SourceFiles("*.axaml")))
        {
            var name = Path.GetFileName(file);
            if (name is "Palette.cs" or "Language.cs")
            {
                continue; // the palette defines the colors; the language flags are artwork, not theme
            }

            var text = File.ReadAllText(file);
            if (HexColor().IsMatch(text) || ColorParsing().IsMatch(text) || (file.EndsWith(".axaml") && NamedColorAttribute().IsMatch(text)))
            {
                offenders.Add(name);
            }
        }

        // Assert
        Assert.Empty(offenders);
    }

    private static readonly IReadOnlyDictionary<string, Avalonia.Media.Color> NoOverrides = new Dictionary<string, Avalonia.Media.Color>();

    [AvaloniaFact]
    public void Apply_Overrides_ChangeTheSharedBrushesAndTheRestKeepTheirBuiltInColor()
    {
        // Arrange
        var builtIn = Palette.Tokens.ToDictionary(token => token.Key, token => token.Value.Color);
        var sameBrushes = Palette.Tokens.ToDictionary(token => token.Key, token => token.Value);
        try
        {
            // Act
            Palette.Apply(new Dictionary<string, Avalonia.Media.Color>
            {
                ["Text"] = Avalonia.Media.Color.Parse("#E0E0E0"),
                ["Positive"] = Avalonia.Media.Color.Parse("#00FF00"),
            });

            // Assert
            Assert.Equal(Avalonia.Media.Color.Parse("#E0E0E0"), Palette.Text.Color);
            Assert.Equal(Avalonia.Media.Color.Parse("#00FF00"), Palette.Positive.Color);
            Assert.Equal(builtIn["Negative"], Palette.Negative.Color);
            Assert.All(Palette.Tokens, token => Assert.Same(sameBrushes[token.Key], token.Value));
        }
        finally
        {
            Palette.Apply(NoOverrides);
        }
    }

    [AvaloniaFact]
    public void Apply_NoOverridesAfterATheme_RestoresEveryBuiltInColor()
    {
        // Arrange
        var builtIn = Palette.Tokens.ToDictionary(token => token.Key, token => token.Value.Color);
        Palette.Apply(new Dictionary<string, Avalonia.Media.Color> { ["Text"] = Avalonia.Media.Color.Parse("#E0E0E0"), ["Border"] = Avalonia.Media.Color.Parse("#010203") });

        // Act
        Palette.Apply(NoOverrides);

        // Assert
        Assert.Equal(builtIn, Palette.Tokens.ToDictionary(token => token.Key, token => token.Value.Color));
    }

    [AvaloniaFact]
    public void Apply_Overrides_AlsoChangeWhatTheXamlResourcesHold()
    {
        try
        {
            // Act
            Palette.Apply(new Dictionary<string, Avalonia.Media.Color> { ["WindowBackground"] = Avalonia.Media.Color.Parse("#102030") });

            // Assert
            Assert.True(Application.Current!.Resources.TryGetResource(Palette.ResourceKey("WindowBackground"), null, out var resource));
            Assert.Equal(Avalonia.Media.Color.Parse("#102030"), ((Avalonia.Media.SolidColorBrush)resource!).Color);
        }
        finally
        {
            Palette.Apply(NoOverrides);
        }
    }

    [AvaloniaFact]
    public async Task Controls_DeclaredInXaml_UseTheSharedPaletteBrushes()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        var window = workspace.CreateSeparateFolderWindow();
        await DesktopWorkspace.OpenAsync(window);
        var preview = new CsvImportPreviewWindow();
        preview.Show();

        // Assert
        Assert.Same(Palette.WindowBackground, window.Background);
        Assert.Same(Palette.Error, preview.FindControl<TextBlock>("ErrorText")!.Foreground);
        Assert.Same(Palette.TextBody, preview.FindControl<SelectableTextBlock>("MessageText")!.Foreground);
        preview.Close();
    }

    [AvaloniaFact]
    public void StatCards_OfTheOverview_ColorIncomeAndExpensesWithThePalette()
    {
        // Arrange
        var view = new OverviewView();
        new Window { Content = view }.Show();

        // Assert
        Assert.Same(Palette.Positive, view.FindControl<StatCard>("IncomeCard")!.Foreground);
        Assert.Same(Palette.Negative, view.FindControl<StatCard>("ExpensesCard")!.Foreground);
        Assert.Same(Palette.Negative, view.FindControl<TextBlock>("SummaryLabel")!.Foreground);
    }
}