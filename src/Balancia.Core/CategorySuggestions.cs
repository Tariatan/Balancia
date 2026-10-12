namespace Balancia.Core;

public enum CategorySuggestionSection
{
    TopMatch,
    Regular,
    Other,
}

public sealed record CategorySuggestion(string Path, CategorySuggestionSection? Section, bool IsKeyboardSelected = false);

public static class CategorySuggestionRanking
{
    public static IReadOnlyList<CategorySuggestion> Build(
        IEnumerable<string> categoryPaths,
        IEnumerable<string> regularCategoryPaths,
        string query)
    {
        var normalizedQuery = query.Trim();
        var matches = categoryPaths
            .Where(path => normalizedQuery.Length == 0 || path.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var suggestions = new List<CategorySuggestion>();
        var matchingSet = matches.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var regularMatches = regularCategoryPaths
            .Where(matchingSet.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToArray();
        var regularSet = regularMatches.ToHashSet(StringComparer.OrdinalIgnoreCase);
        suggestions.AddRange(regularMatches.Select((path, index) =>
            new CategorySuggestion(path, index == 0 ? CategorySuggestionSection.Regular : null)));
        string? topMatch = null;

        if (normalizedQuery.Length > 0)
        {
            topMatch = matches
                .Where(path => !regularSet.Contains(path))
                .OrderBy(path => MatchRank(path, normalizedQuery))
                .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (topMatch is not null)
            {
                suggestions.Add(new CategorySuggestion(topMatch, CategorySuggestionSection.TopMatch));
            }
        }

        var remaining = matches
            .Where(path => !string.Equals(path, topMatch, StringComparison.OrdinalIgnoreCase) && !regularSet.Contains(path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        suggestions.AddRange(remaining.Select((path, index) =>
            new CategorySuggestion(path, index == 0 ? CategorySuggestionSection.Other : null)));
        return suggestions;
    }

    public static IReadOnlyList<CategorySuggestion> Highlight(IReadOnlyList<CategorySuggestion> suggestions, int selectedIndex) =>
        [.. suggestions.Select((suggestion, index) => suggestion with { IsKeyboardSelected = index == selectedIndex })];

    private static int MatchRank(string path, string query)
    {
        if (string.Equals(path, query, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (path.StartsWith(query, StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        return path.Split('/').Any(part => part.TrimStart().StartsWith(query, StringComparison.OrdinalIgnoreCase)) ? 2 : 3;
    }
}
