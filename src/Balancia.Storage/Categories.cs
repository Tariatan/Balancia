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
        if (name.Length == 0 || name.Contains('/'))
        {
            throw new ArgumentException("Enter a category name without '/'; choose its parent separately.");
        }

        if (parentId is not null && parentId == id)
        {
            throw new ArgumentException("A category cannot be its own parent.");
        }

        var key = id ?? Guid.NewGuid().ToString("N");
        Write((c, tx) =>
        {
            if (id is not null)
            {
                RequireCategory(c, tx, id);
            }

            if (parentId is not null)
            {
                LedgerSql.Require(c, tx, "SELECT 1 FROM categories WHERE id=$id AND parent_id IS NULL", parentId, "Choose a top-level parent category.");
                var parentArchived = Convert.ToInt64(LedgerSql.Scalar(c, tx, "SELECT archived FROM categories WHERE id=$id", ("$id", parentId))) != 0;
                var previousParent = id is null ? null : LedgerSql.Scalar(c, tx, "SELECT parent_id FROM categories WHERE id=$id", ("$id", id)) as string;
                if (parentArchived && (!archived || previousParent != parentId))
                {
                    throw new ArgumentException("Restore the parent category before adding or restoring a child.");
                }

                if (HasSubcategories(c, tx, key))
                {
                    throw new ArgumentException("A category with subcategories must remain top-level.");
                }
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
        if (HasSubcategories(c, tx, id))
        {
            throw new InvalidOperationException("A category with subcategories cannot be deleted. Archive it instead.");
        }

        if (IsUsedByTransactions(c, tx, id))
        {
            throw new InvalidOperationException("A category used by transactions cannot be deleted. Archive it instead.");
        }

        LedgerSql.Execute(c, tx, "DELETE FROM categories WHERE id=$id", ("$id", id));
    });

    public bool CategoryHasSubcategories(string id) => ReadDeferred((c, tx) => HasSubcategories(c, tx, id));

    public bool CategoryIsUsedByTransactions(string id) => ReadDeferred((c, tx) => IsUsedByTransactions(c, tx, id));

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

    private static bool HasSubcategories(SqliteConnection c, SqliteTransaction tx, string id) =>
        LedgerSql.Scalar(c, tx, "SELECT 1 FROM categories WHERE parent_id=$id LIMIT 1", ("$id", id)) is not null;

    private static bool IsUsedByTransactions(SqliteConnection c, SqliteTransaction tx, string id) =>
        LedgerSql.Scalar(c, tx, "SELECT 1 FROM ledger WHERE category_id=$id LIMIT 1", ("$id", id)) is not null;

    private static string[] ParseCategoryPath(string? categoryPath)
    {
        if (string.IsNullOrWhiteSpace(categoryPath))
        {
            return [];
        }

        var parts = categoryPath.Split('/').Select(part => part.Trim()).ToArray();
        if (parts.Length > 2 || parts.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Enter a category or Category / Subcategory with one name on each side of '/'.");
        }

        return parts;
    }

    private static List<Category> ReadCategories(SqliteConnection c, SqliteTransaction tx)
    {
        var categories = new List<Category>();
        using var cmd = LedgerSql.Command(c, tx, "SELECT c.id,c.name,c.parent_id,CASE WHEN p.id IS NULL THEN c.name ELSE p.name || ' / ' || c.name END,c.archived FROM categories c LEFT JOIN categories p ON p.id=c.parent_id ORDER BY 4 COLLATE NOCASE");
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            categories.Add(new Category(r.GetString(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2), r.GetString(3), r.GetBoolean(4)));
        }

        return categories;
    }
}
