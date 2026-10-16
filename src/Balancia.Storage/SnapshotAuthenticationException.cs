namespace Balancia.Storage;

/// <summary>The snapshot could not be authenticated: the passphrase is incorrect or the file was damaged.</summary>
public sealed class SnapshotAuthenticationException() : InvalidOperationException("The snapshot passphrase is incorrect or the file is damaged.");
