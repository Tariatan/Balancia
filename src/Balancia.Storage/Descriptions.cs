namespace Balancia.Storage;

public sealed partial class LedgerStore
{
    private const string RecentDescriptionsSql = """
        SELECT description FROM ledger
        WHERE kind <> 'OpeningBalance' AND trim(description) <> ''
        GROUP BY description
        ORDER BY MAX(date) DESC, MAX(rowid) DESC;
        """;

    /// <summary>Distinct descriptions (case-insensitive), most recently used first.</summary>
    public IReadOnlyList<string> ReadRecentDescriptions()
    {
        using var connection = connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = RecentDescriptionsSql;
        using var reader = command.ExecuteReader();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var descriptions = new List<string>();
        while (reader.Read())
        {
            var description = reader.GetString(0);
            if (seen.Add(description))
            {
                descriptions.Add(description);
            }
        }

        return descriptions;
    }
}
