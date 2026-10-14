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
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var regularMatches = regularCategoryPaths
            .Where(matches.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToArray();
        var regularSet = regularMatches.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var others = matches
            .Where(path => !regularSet.Contains(path))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var topIndex = FindTopMatchIndex(others, normalizedQuery);
        var suggestions = new List<CategorySuggestion>(regularMatches.Length + others.Count);

        foreach (var (index, path) in regularMatches.Index())
        {
            suggestions.Add(new CategorySuggestion(path, index == 0 ? CategorySuggestionSection.Regular : null));
        }

        if (topIndex >= 0)
        {
            suggestions.Add(new CategorySuggestion(others[topIndex], CategorySuggestionSection.TopMatch));
        }

        var isFirstOther = true;

        foreach (var (index, path) in others.Index())
        {
            if (index == topIndex)
            {
                continue;
            }

            suggestions.Add(new CategorySuggestion(path, isFirstOther ? CategorySuggestionSection.Other : null));
            isFirstOther = false;
        }

        return suggestions;
    }

    public static IReadOnlyList<CategorySuggestion> Highlight(IReadOnlyList<CategorySuggestion> suggestions, int selectedIndex) =>
        [.. suggestions.Select((suggestion, index) => suggestion with { IsKeyboardSelected = index == selectedIndex })];

    // Candidates arrive sorted by path, so the first path with the lowest rank wins ties alphabetically.
    private static int FindTopMatchIndex(List<string> sortedCandidates, string query)
    {
        if (query.Length == 0)
        {
            return -1;
        }

        var topIndex = -1;
        var topRank = int.MaxValue;

        foreach (var (index, path) in sortedCandidates.Index())
        {
            var rank = MatchRank(path, query);

            if (rank < topRank)
            {
                topIndex = index;
                topRank = rank;
            }
        }

        return topIndex;
    }

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

        return AnySegmentStartsWith(path, query) ? 2 : 3;
    }

    private static bool AnySegmentStartsWith(ReadOnlySpan<char> path, ReadOnlySpan<char> query)
    {
        while (true)
        {
            var separator = path.IndexOf('/');
            var segment = separator < 0 ? path : path[..separator];

            if (segment.TrimStart().StartsWith(query, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (separator < 0)
            {
                return false;
            }

            path = path[(separator + 1)..];
        }
    }
}
