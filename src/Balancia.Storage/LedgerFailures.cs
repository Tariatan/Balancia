using Microsoft.Data.Sqlite;

namespace Balancia.Storage;

/// <summary>Classifies database failures, so Desktop and Android can react to them without referencing SQLite types or its error codes.</summary>
public static class LedgerFailures
{
    private const int ConstraintViolation = 19;
    private const int CorruptDatabase = 11;
    private const int NotADatabase = 26;

    /// <summary>Any failure raised by the database engine (a constraint, a lock, an I/O or a corruption error).</summary>
    public static bool IsDatabaseFailure(Exception exception) => exception is SqliteException;

    /// <summary>A write that conflicts with existing data, such as a duplicate name or a reference to a missing row.</summary>
    public static bool IsConstraintViolation(Exception exception) =>
        exception is SqliteException { SqliteErrorCode: ConstraintViolation };

    /// <summary>The file is not a usable ledger: damaged, or not a SQLite database at all.</summary>
    public static bool IsUnreadableDatabase(Exception exception) =>
        exception is SqliteException { SqliteErrorCode: CorruptDatabase or NotADatabase };
}
