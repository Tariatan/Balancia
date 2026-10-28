using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Balancia.Core;
using Xunit;

namespace Balancia.Desktop.Tests;

public sealed partial class PaletteTests
{
    [AvaloniaFact]
    public async Task Overview_ListText_UsesTheTransactionTextRole()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        var store = workspace.SeedStore();
        var account = LedgerSeed.Account(store, "Demo daily");
        var parent = store.SaveCategory(null, "Food", null);
        var category = store.SaveCategory(null, "Groceries", parent);
        store.SaveTransaction(null, new TransactionDraft(TransactionKind.Expense, LedgerSeed.Today,
            "Demo groceries", Money.FromFrancs(24.80m), account, CategoryId: category));

        // Act
        var window = await workspace.OpenWindowAsync();
        window.UpdateLayout();

        // Assert
        foreach (var name in (string[])["AccountsCard", "CategoriesCard", "TopExpendituresCard"])
        {
            var labels = window.Find<UserControl>(name).GetVisualDescendants().OfType<TextBlock>()
                .Where(label => label.Text is "Demo daily" or "Food" or "Groceries").ToArray();
            Assert.NotEmpty(labels);
            Assert.All(labels, label => Assert.Same(Palette.Text, label.Foreground));
            foreach (var label in labels)
            {
                var row = label.FindAncestorOfType<ListBoxItem>();
                if (row is null)
                {
                    continue;
                }

                var point = label.TranslatePoint(new Point(label.Bounds.Width / 2, label.Bounds.Height / 2), window)!.Value;
                window.MouseMove(point);
                Assert.True(row.IsPointerOver);
                Assert.Same(Palette.Text, label.Foreground);
                window.MouseDown(point, MouseButton.Left);
                Assert.Same(Palette.Text, label.Foreground);
                window.MouseUp(point, MouseButton.Left);
            }
        }
    }

    [AvaloniaFact]
    public void Calendar_DayCells_UsePaletteTextAndSurfaces()
    {
        // Arrange
        var calendar = new Calendar();
        var window = new Window { Content = calendar };

        // Act
        window.Show();
        window.UpdateLayout();

        // Assert
        try
        {
            var days = calendar.GetVisualDescendants().OfType<CalendarDayButton>().ToArray();
            Assert.NotEmpty(days);
            Assert.All(days, day =>
            {
                Assert.Same(Palette.Text, day.Foreground);
                Assert.Same(Palette.Surface, day.Background);
            });
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Dialogs_AndFilterCheckboxStates_UseTheLoadablePalette()
    {
        // Arrange
        var check = new CheckBox { Classes = { "compact-filter-checkbox" } };
        var text = new TextBlock { Text = "Demo account" };
        var button = new Button { Content = "Save" };
        var input = new TextBox { Text = "Demo" };
        var window = new Window { Content = new StackPanel { Children = { text, check, button, input } } };
        window.Show();

        try
        {
            // Act
            Palette.Apply(new Dictionary<string, Avalonia.Media.Color>
            {
                ["Text"] = Avalonia.Media.Color.Parse("#E6EAF0"),
                ["Surface"] = Avalonia.Media.Color.Parse("#20262E"),
            });

            // Assert
            Assert.Same(Palette.Surface, window.Background);
            Assert.Same(Palette.Text, text.Foreground);
            Assert.Same(Palette.Text, button.Foreground);
            Assert.Same(Palette.Surface, input.Background);
            foreach (var value in (bool?[])[false, true, null])
            {
                check.IsChecked = value;
                window.UpdateLayout();
                var rectangle = check.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "NormalRectangle");
                Assert.Same(Palette.Text, rectangle.BorderBrush);
            }

            foreach (var dialog in (Window[])[new SettingsWindow(), new CsvImportPreviewWindow(), new ErrorDialog(), new SnapshotPassphraseWindow()])
            {
                dialog.Show();
                Assert.Same(Palette.Surface, dialog.Background);
                Assert.Same(Palette.Text, dialog.Foreground);
                dialog.Close();
            }
        }
        finally
        {
            window.Close();
            Palette.Apply(NoOverrides);
        }
    }

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