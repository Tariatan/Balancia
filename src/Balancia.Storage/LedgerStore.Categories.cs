using Balancia.Core;
using Microsoft.Data.Sqlite;

namespace Balancia.Storage;

public sealed partial class LedgerStore
{
    public void RememberCategorySelection(string categoryId) => LogOperation(() =>
    {
        using var connection = connections.Open();
        using var transaction = connection.BeginTransaction();
        Require(connection, transaction, "SELECT 1 FROM categories WHERE id=$id AND archived=0", categoryId,
            "Choose an active category.");
        var revision = checked(Convert.ToInt64(Scalar(connection, transaction, "SELECT revision FROM metadata WHERE id=1")) + 1);
        Execute(connection, transaction, "UPDATE metadata SET revision=$revision WHERE id=1", ("$revision", revision));
        Execute(connection, transaction, """
            INSERT INTO category_selections(category_id,selected_order) VALUES($id,$order)
            ON CONFLICT(category_id) DO UPDATE SET selected_order=excluded.selected_order;
            """, ("$id", categoryId), ("$order", revision));
        transaction.Commit();
        return true;
    });

    public IReadOnlyList<string> ReadRecentCategoryPaths()
    {
        using var connection = connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT CASE WHEN parent.id IS NULL THEN category.name ELSE parent.name || ' / ' || category.name END AS path
            FROM category_selections selection
            JOIN categories category ON category.id=selection.category_id
            LEFT JOIN categories parent ON parent.id=category.parent_id
            WHERE category.archived=0 AND (parent.id IS NULL OR parent.archived=0)
            ORDER BY selection.selected_order DESC, path COLLATE NOCASE;
            """;
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

        var key = id ?? Guid.NewGuid().ToString("N");
        Write((c, tx) =>
        {
            if (id is not null)
            {
                Require(c, tx, "SELECT 1 FROM categories WHERE id=$id", id, "Category no longer exists.");
            }

            if (parentId is not null)
            {
                if (parentId == key)
                {
                    throw new ArgumentException("A category cannot be its own parent.");
                }

                Require(c, tx, "SELECT 1 FROM categories WHERE id=$id AND parent_id IS NULL", parentId, "Choose a top-level parent category.");
                var parentArchived = Convert.ToInt64(Scalar(c, tx, "SELECT archived FROM categories WHERE id=$id", ("$id", parentId))) != 0;
                var previousParent = id is null ? null : Scalar(c, tx, "SELECT parent_id FROM categories WHERE id=$id", ("$id", id)) as string;
                if (parentArchived && (!archived || previousParent != parentId))
                {
                    throw new ArgumentException("Restore the parent category before adding or restoring a child.");
                }

                if (Scalar(c, tx, "SELECT 1 FROM categories WHERE parent_id=$id LIMIT 1", ("$id", key)) is not null)
                {
                    throw new ArgumentException("A category with subcategories must remain top-level.");
                }
            }
            Execute(c, tx, """
                INSERT INTO categories VALUES($id,$name,$parent,$archived)
                ON CONFLICT(id) DO UPDATE SET name=excluded.name,parent_id=excluded.parent_id,archived=excluded.archived;
                """, ("$id", key), ("$name", name), ("$parent", parentId), ("$archived", archived ? 1 : 0));
            if (archived)
            {
                Execute(c, tx, "UPDATE categories SET archived=1 WHERE parent_id=$id", ("$id", key));
            }
        });
        return key;
    }

    public void DeleteCategory(string id) => Write((c, tx) =>
    {
        Require(c, tx, "SELECT 1 FROM categories WHERE id=$id", id, "Category no longer exists.");
        if (HasSubcategories(c, tx, id))
        {
            throw new InvalidOperationException("A category with subcategories cannot be deleted. Archive it instead.");
        }

        if (IsUsedByTransactions(c, tx, id))
        {
            throw new InvalidOperationException("A category used by transactions cannot be deleted. Archive it instead.");
        }

        Execute(c, tx, "DELETE FROM categories WHERE id=$id", ("$id", id));
    });

    public bool CategoryHasSubcategories(string id)
    {
        using var c = connections.Open();
        using var tx = c.BeginTransaction(deferred: true);
        var result = HasSubcategories(c, tx, id);
        tx.Commit();
        return result;
    }

    public bool CategoryIsUsedByTransactions(string id)
    {
        using var c = connections.Open();
        using var tx = c.BeginTransaction(deferred: true);
        var result = IsUsedByTransactions(c, tx, id);
        tx.Commit();
        return result;
    }

    private static bool HasSubcategories(SqliteConnection c, SqliteTransaction tx, string id) =>
        Scalar(c, tx, "SELECT 1 FROM categories WHERE parent_id=$id LIMIT 1", ("$id", id)) is not null;

    private static bool IsUsedByTransactions(SqliteConnection c, SqliteTransaction tx, string id) =>
        Scalar(c, tx, "SELECT 1 FROM ledger WHERE category_id=$id LIMIT 1", ("$id", id)) is not null;

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
        using var cmd = Command(c, tx, "SELECT c.id,c.name,c.parent_id,CASE WHEN p.id IS NULL THEN c.name ELSE p.name || ' / ' || c.name END,c.archived FROM categories c LEFT JOIN categories p ON p.id=c.parent_id ORDER BY 4 COLLATE NOCASE");
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            categories.Add(new Category(r.GetString(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2), r.GetString(3), r.GetBoolean(4)));
        }

        return categories;
    }
}
