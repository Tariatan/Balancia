namespace Balancia.Core;

public enum CategorySuggestionSection
{
    TopMatch,
    Recent,
    Other,
}

public sealed record CategorySuggestion(string Path, CategorySuggestionSection? Section, bool IsKeyboardSelected = false);

public static class CategorySuggestionRanking
{
    public static IReadOnlyList<CategorySuggestion> Build(
        IEnumerable<string> categoryPaths,
        IEnumerable<string> recentCategoryPaths,
        string query)
    {
        var normalizedQuery = query.Trim();
        var matches = categoryPaths
            .Where(path => normalizedQuery.Length == 0 || path.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var suggestions = new List<CategorySuggestion>();
        string? topMatch = null;

        if (normalizedQuery.Length > 0)
        {
            topMatch = matches
                .OrderBy(path => MatchRank(path, normalizedQuery))
                .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (topMatch is not null)
            {
                suggestions.Add(new CategorySuggestion(topMatch, CategorySuggestionSection.TopMatch));
            }
        }

        var recentMatches = recentCategoryPaths
            .Where(path => matches.Contains(path, StringComparer.OrdinalIgnoreCase))
            .Where(path => !string.Equals(path, topMatch, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        suggestions.AddRange(recentMatches.Select((path, index) =>
            new CategorySuggestion(path, index == 0 ? CategorySuggestionSection.Recent : null)));

        var recentSet = recentMatches.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var remaining = matches
            .Where(path => !string.Equals(path, topMatch, StringComparison.OrdinalIgnoreCase) && !recentSet.Contains(path))
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
