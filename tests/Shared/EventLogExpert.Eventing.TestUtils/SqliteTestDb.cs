// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Microsoft.Data.Sqlite;

namespace EventLogExpert.Eventing.TestUtils;

/// <summary>
/// Helpers for managing SQLite test database files.
/// </summary>
public static class SqliteTestDb
{
    /// <summary>
    /// Deletes a SQLite test database file with retries, after first releasing pooled
    /// SqliteConnection handles.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="EventLogExpert.Eventing.ProviderDatabase.ProviderDbContext"/> uses
    /// EF Core's default pooled SqliteConnection. Without
    /// <see cref="SqliteConnection.ClearAllPools"/> the pooled handle still owns the file
    /// after the context is disposed and <see cref="File.Delete"/> hits a sharing violation.
    /// </para>
    /// <para>
    /// <see cref="SqliteConnection.ClearAllPools"/> mutates process-wide state, so it must
    /// only be called from a test assembly configured for serial execution
    /// (xunit.runner.json with <c>parallelizeTestCollections: false</c>) — otherwise it can
    /// race with concurrent SQLite work on another thread.
    /// </para>
    /// </remarks>
    public static void Delete(string? path, int maxAttempts = 10, int delayMs = 200)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) { return; }

        SqliteConnection.ClearAllPools();

        for (int i = 0; i < maxAttempts; i++)
        {
            try
            {
                File.Delete(path);
                return;
            }
            catch (IOException) when (i < maxAttempts - 1)
            {
                Thread.Sleep(delayMs);
            }
            catch (UnauthorizedAccessException) when (i < maxAttempts - 1)
            {
                Thread.Sleep(delayMs);
            }
            catch (IOException)
            {
                // Best-effort cleanup; swallow last IOException — OS will reclaim temp files.
                return;
            }
            catch (UnauthorizedAccessException)
            {
                return;
            }
        }
    }
}
