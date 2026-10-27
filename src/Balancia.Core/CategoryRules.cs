using System.Diagnostics.CodeAnalysis;

namespace Balancia.Core;

/// <summary>
/// The rules for categories, so that the ledger store, the dialogs and the CSV import enforce one definition.
/// A category path is <c>Name</c> or <c>Parent / Child</c>; a top-level category has no parent and is the only kind that may have children.
/// The checks run over a plain list of categories, which the store reads inside its write transaction and Desktop takes from its snapshot.
/// </summary>
public static class CategoryRules
{
    public static readonly string InvalidPathMessage = "Enter a category or Category / Subcategory with one name on each side of '/'.";

    public static readonly string InvalidNameMessage = "Enter a category name without '/'; choose its parent separately.";

    public static readonly string OwnParentMessage = "A category cannot be its own parent.";

    public static readonly string ParentNotTopLevelMessage = "Choose a top-level parent category.";

    public static readonly string ParentArchivedMessage = "Restore the parent category before adding or restoring a child.";

    public static readonly string MustRemainTopLevelMessage = "A category with subcategories must remain top-level.";

    public static readonly string DuplicateNameMessage = "A category with this name already exists under the same parent.";

    public static readonly string DeleteWithSubcategoriesMessage = "A category with subcategories cannot be deleted. Archive it instead.";

    public static readonly string DeleteUsedMessage = "A category used by transactions cannot be deleted. Archive it instead.";

    /// <summary>
    /// Splits a typed category path into its trimmed names: none for empty text, otherwise one or two. False when the path has more than two
    /// levels or an empty side; <paramref name="names"/> is then null. Whether the categories exist is not checked here.
    /// </summary>
    public static bool TryParsePath(string? path, [NotNullWhen(true)] out string[]? names)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            names = [];
            return true;
        }

        var parts = path.Split('/').Select(part => part.Trim()).ToArray();
        if (parts.Length > 2 || parts.Any(string.IsNullOrWhiteSpace))
        {
            names = null;
            return false;
        }

        names = parts;
        return true;
    }

    /// <summary>
    /// Why saving the category cannot go ahead, or null when it can. <paramref name="id"/> is null for a new category;
    /// <paramref name="categories"/> holds every existing category, including the one being edited.
    /// </summary>
    public static string? CheckSave(IReadOnlyList<Category> categories, string? id, string name, string? parentId, bool archived)
    {
        var trimmedName = name.Trim();
        if (trimmedName.Length == 0 || trimmedName.Contains('/'))
        {
            return InvalidNameMessage;
        }

        if (parentId is not null && parentId == id)
        {
            return OwnParentMessage;
        }

        if (parentId is not null)
        {
            var parent = categories.FirstOrDefault(c => c.Id == parentId);
            if (parent is not { ParentId: null })
            {
                return ParentNotTopLevelMessage;
            }

            var previousParentId = categories.FirstOrDefault(c => c.Id == id)?.ParentId;
            if (parent.Archived && (!archived || previousParentId != parentId))
            {
                return ParentArchivedMessage;
            }

            if (id is not null && categories.Any(c => c.ParentId == id))
            {
                return MustRemainTopLevelMessage;
            }
        }

        return categories.Any(c => c.Id != id && c.ParentId == parentId && string.Equals(c.Name, trimmedName, StringComparison.OrdinalIgnoreCase))
            ? DuplicateNameMessage
            : null;
    }

    /// <summary>Why the category cannot be deleted (archive it instead), or null when it can.</summary>
    public static string? CheckDelete(IReadOnlyList<Category> categories, string id)
    {
        if (categories.Any(c => c.ParentId == id))
        {
            return DeleteWithSubcategoriesMessage;
        }

        return categories.Any(c => c.Id == id && c.HasTransactions) ? DeleteUsedMessage : null;
    }
}
