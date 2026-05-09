// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Eventing.ProviderDatabase;

public sealed class DatabaseUpgradeException : Exception
{
    public DatabaseUpgradeException(string databasePath, string reason)
        : base($"Database upgrade failed for '{databasePath}': {reason}")
    {
        DatabasePath = databasePath;
        Reason = reason;
    }

    public DatabaseUpgradeException(string databasePath, string reason, Exception innerException)
        : base($"Database upgrade failed for '{databasePath}': {reason}", innerException)
    {
        DatabasePath = databasePath;
        Reason = reason;
    }

    public string DatabasePath { get; }

    public string Reason { get; }
}
