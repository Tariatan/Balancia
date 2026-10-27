using Balancia.Core;
using Microsoft.Data.Sqlite;

namespace Balancia.Storage;

public sealed partial class LedgerStore
{
    private const string CategoryExistsSql = "SELECT 1 FROM categories WHERE id=$id";

    // Return ranked candidates; the dropdown filters by text before taking its two Regular entries.
    public IReadOnlyList<string> ReadRegularCategoryPaths()
    {
        var today = Today;
        using var connection = connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT CASE WHEN parent.id IS NULL THEN category.name ELSE parent.name || ' / ' || category.name END AS path
            FROM ledger
            JOIN categories category ON category.id=ledger.category_id
            LEFT JOIN categories parent ON parent.id=category.parent_id
            WHERE ledger.kind IN ('Expense','Income')
                AND ledger.date BETWEEN $from AND $through
                AND category.archived=0 AND (parent.id IS NULL OR parent.archived=0)
            GROUP BY category.id
            ORDER BY COUNT(*) DESC, MAX(ledger.date) DESC, path COLLATE NOCASE;
            """;
        command.Parameters.AddWithValue("$from", LedgerSql.DateText(today.AddDays(-34)));
        command.Parameters.AddWithValue("$through", LedgerSql.DateText(today));
        using var reader = command.ExecuteReader();
        var paths = new List<string>();

        while (reader.Read())
        {
            paths.Add(reader.GetString(0));
        }

        return paths;
    }

    public string SaveCategory(string? id, string name, string? parentId, bool archived = false)
    {
        ArgumentNullException.ThrowIfNull(name);
        name = name.Trim();
        var key = id ?? Guid.NewGuid().ToString("N");
        Write((c, tx) =>
        {
            if (id is not null)
            {
                RequireCategory(c, tx, id);
            }

            if (CategoryRules.CheckSave(ReadCategories(c, tx), id, name, parentId, archived) is { } message)
            {
                throw new ArgumentException(message);
            }

            LedgerSql.Execute(c, tx, """
                INSERT INTO categories VALUES($id,$name,$parent,$archived)
                ON CONFLICT(id) DO UPDATE SET name=excluded.name,parent_id=excluded.parent_id,archived=excluded.archived;
                """, ("$id", key), ("$name", name), ("$parent", parentId), ("$archived", archived ? 1 : 0));
            if (archived)
            {
                LedgerSql.Execute(c, tx, "UPDATE categories SET archived=1 WHERE parent_id=$id", ("$id", key));
            }
        });
        return key;
    }

    public void DeleteCategory(string id) => Write((c, tx) =>
    {
        RequireCategory(c, tx, id);
        if (CategoryRules.CheckDelete(ReadCategories(c, tx), id) is { } message)
        {
            throw new InvalidOperationException(message);
        }

        LedgerSql.Execute(c, tx, "DELETE FROM categories WHERE id=$id", ("$id", id));
    });

    private T ReadDeferred<T>(Func<SqliteConnection, SqliteTransaction, T> read)
    {
        using var c = connections.Open();
        using var tx = c.BeginTransaction(deferred: true);
        var result = read(c, tx);
        tx.Commit();
        return result;
    }

    private static void RequireCategory(SqliteConnection c, SqliteTransaction tx, string id) =>
        LedgerSql.Require(c, tx, CategoryExistsSql, id, "Category no longer exists.");

    private static string[] ParseCategoryPath(string? categoryPath) =>
        CategoryRules.TryParsePath(categoryPath, out var names) ? names : throw new ArgumentException(CategoryRules.InvalidPathMessage);

    private static List<Category> ReadCategories(SqliteConnection c, SqliteTransaction tx)
    {
        var categories = new List<Category>();
        using var cmd = LedgerSql.Command(c, tx, "SELECT c.id,c.name,c.parent_id,CASE WHEN p.id IS NULL THEN c.name ELSE p.name || ' / ' || c.name END,c.archived,EXISTS(SELECT 1 FROM ledger l WHERE l.category_id=c.id) FROM categories c LEFT JOIN categories p ON p.id=c.parent_id ORDER BY 4 COLLATE NOCASE");
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            categories.Add(new Category(r.GetString(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2), r.GetString(3), r.GetBoolean(4), r.GetBoolean(5)));
        }

        return categories;
    }
}
