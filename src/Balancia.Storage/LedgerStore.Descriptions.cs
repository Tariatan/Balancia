namespace Balancia.Storage;

public sealed partial class LedgerStore
{
    public IReadOnlyList<string> ReadRecentDescriptions()
    {
        using var connection = connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT description FROM ledger
            WHERE kind <> 'OpeningBalance' AND trim(description) <> ''
            GROUP BY description
            ORDER BY MAX(date) DESC, MAX(rowid) DESC;
            """;
        using var reader = command.ExecuteReader();
        var descriptions = new List<string>();
        while (reader.Read())
        {
            descriptions.Add(reader.GetString(0));
        }

        return descriptions.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
