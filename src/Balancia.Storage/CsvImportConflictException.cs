namespace Balancia.Storage;

/// <summary>A CSV import cannot be applied: the file, the ledger or a prior import changed or disagrees with the batch.</summary>
public sealed class CsvImportConflictException(string message) : InvalidOperationException(message);
