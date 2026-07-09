// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Provider.Schema;

/// <summary>
///     Thrown when a schema probe could not acquire the cross-process schema lock within its timeout - typically
///     because another process is mid-upgrade. Callers must treat this as transient/retryable (re-probe later), never
///     mapping it to a definitive classification such as Unknown, UnrecognizedSchema, or NeedsUpgrade.
/// </summary>
public sealed class SchemaLockTimeoutException : Exception
{
    public SchemaLockTimeoutException(string databasePath)
        : base($"Timed out acquiring the schema lock for '{databasePath}'; another process may be upgrading it.") =>
        DatabasePath = databasePath;

    public SchemaLockTimeoutException(string databasePath, Exception innerException)
        : base($"Timed out acquiring the schema lock for '{databasePath}'; another process may be upgrading it.", innerException) =>
        DatabasePath = databasePath;

    public string DatabasePath { get; }
}
