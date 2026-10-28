using System.Text.Json;
using Avalonia.Media;
using Serilog;

namespace Balancia.Desktop;

/// <summary>A named set of colors for <see cref="Palette"/> roles. A role it does not mention keeps its built-in color.</summary>
internal sealed record ColorTheme(string Name, IReadOnlyDictionary<string, Color> Colors)
{
    /// <summary>The name of the theme that is built in; a theme file cannot take it.</summary>
    internal const string BuiltInName = "Balancia";

    /// <summary>The built-in theme: no overrides, so every role has its built-in color.</summary>
    internal static ColorTheme BuiltIn { get; } = new(BuiltInName, new Dictionary<string, Color>());

    public override string ToString() => Name;
}

/// <summary>What reading one theme file produced: the theme, or null when the file is unusable, and what was ignored on the way.</summary>
internal sealed record ColorThemeFileResult(ColorTheme? Theme, IReadOnlyList<string> Warnings);

/// <summary>
/// Reads a theme file: <c>{ "name": "Dusk", "colors": { "Text": "#RRGGBB", ... } }</c>. The name is optional (the file name is used), role names
/// ignore letter case, and colors are <c>#RRGGBB</c> or <c>#AARRGGBB</c>. An unknown role or an unreadable color is ignored with a warning, so a
/// file with a mistake still changes what it can. Only a file that is not a JSON object with a <c>colors</c> object is refused.
/// </summary>
internal static class ColorThemeFile
{
    internal static ColorThemeFileResult Parse(string json, string fileName)
    {
        var warnings = new List<string>();
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("colors", out var colors) || colors.ValueKind != JsonValueKind.Object)
            {
                return new ColorThemeFileResult(null, ["The file must be a JSON object with a \"colors\" object."]);
            }

            var name = document.RootElement.TryGetProperty("name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String
                ? nameElement.GetString()!.Trim()
                : "";
            if (name.Length == 0)
            {
                name = Path.GetFileNameWithoutExtension(fileName);
            }

            var canonical = Palette.Tokens.Keys.ToDictionary(key => key, key => key, StringComparer.OrdinalIgnoreCase);
            var chosen = new Dictionary<string, Color>();
            foreach (var property in colors.EnumerateObject())
            {
                if (!canonical.TryGetValue(property.Name, out var role))
                {
                    warnings.Add($"Unknown color role '{property.Name}' ignored.");
                }
                else if (property.Value.ValueKind != JsonValueKind.String || !Color.TryParse(property.Value.GetString(), out var color))
                {
                    warnings.Add($"The color of '{role}' is not #RRGGBB or #AARRGGBB; the built-in color is kept.");
                }
                else
                {
                    chosen[role] = color;
                }
            }

            return new ColorThemeFileResult(new ColorTheme(name, chosen), warnings);
        }
        catch (JsonException ex)
        {
            return new ColorThemeFileResult(null, [$"The file is not valid JSON: {ex.Message}"]);
        }
    }
}

/// <summary>The theme files of a folder. A file that cannot be used is skipped and logged, never an error.</summary>
internal static class ColorThemeCatalog
{
    /// <summary>The usable themes of <paramref name="directory"/>, in file name order; none when the folder does not exist.</summary>
    internal static IReadOnlyList<ColorTheme> Load(string? directory)
    {
        if (directory is null || !Directory.Exists(directory))
        {
            return [];
        }

        var themes = new List<ColorTheme>();
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(directory, "*.json").Order(StringComparer.OrdinalIgnoreCase).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warning(ex, "Theme folder unreadable, Directory: '{Directory}'", directory);
            return [];
        }

        foreach (var file in files)
        {
            var fileName = Path.GetFileName(file);
            ColorThemeFileResult result;
            try
            {
                result = ColorThemeFile.Parse(File.ReadAllText(file), fileName);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warning(ex, "Theme file unreadable, File: '{File}'", fileName);
                continue;
            }

            foreach (var warning in result.Warnings)
            {
                Log.Warning("Theme file '{File}': {Warning}", fileName, warning);
            }

            if (result.Theme is not { } theme)
            {
                Log.Warning("Theme file '{File}' ignored", fileName);
            }
            else if (string.Equals(theme.Name, ColorTheme.BuiltInName, StringComparison.OrdinalIgnoreCase)
                || themes.Any(other => string.Equals(other.Name, theme.Name, StringComparison.OrdinalIgnoreCase)))
            {
                Log.Warning("Theme file '{File}' ignored: the name '{Name}' is already taken", fileName, theme.Name);
            }
            else
            {
                themes.Add(theme);
            }
        }

        return themes;
    }
}
