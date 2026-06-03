// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Contracts;

namespace EventLogExpert.ElevationHelper;

/// <summary>
///     Wraps an operation dispatch with destructive-recovery semantics specific to each request type. Runs ENTIRELY
///     in the helper process (elevated; has filesystem access to protected paths). The runner does NO file work.
/// </summary>
/// <remarks>
///     <list type="bullet">
///         <item>
///             <see cref="CreateDatabaseRequest" /> + <see cref="DiffDatabaseRequest" />: dispatched UNWRAPPED.
///             <c>CreateDatabaseOperation</c> / <c>DiffDatabaseOperation</c> fail-fast with <c>Failed</c> when the output
///             path already exists (preserving the user's existing file) and self-clean their own partials via
///             <c>OperationBase.CleanupPartialDatabase</c> on cancellation / runtime failure. A second-layer wrapper here
///             would delete the pre-existing file on the "already-exists" Failed path — data loss the operations
///             themselves take care to avoid.
///         </item>
///         <item>
///             <see cref="UpgradeDatabaseRequest" />: copy <c>target.db</c> → <c>target.db.bak</c> BEFORE the operation.
///             On Outcome != Succeeded, restore from <c>.bak</c> (copy back + delete) and delete it. On Succeeded, delete
///             <c>.bak</c>. Hard-kill mid-operation leaves the <c>.bak</c> file in place; the runner's FailureSummary
///             surfaces this case.
///         </item>
///         <item>
///             <see cref="MergeDatabaseRequest" />: NO new safety code. The operation already wraps destructive work in
///             an EF transaction (MergeDatabaseOperation.cs:108) that rolls back on cancellation via
///             dispose-without-commit.
///         </item>
///         <item><see cref="ShowProvidersRequest" />: read-only — no cleanup needed.</item>
///     </list>
///     The Upgrade wrapper inspects <see cref="DatabaseToolsResult.Outcome" /> rather than relying on
///     <see cref="OperationCanceledException" /> because <c>DatabaseToolsService</c> catches that exception internally and
///     maps it to <see cref="DatabaseToolsOutcome.Cancelled" /> (see DatabaseToolsService.cs:85-94).
/// </remarks>
internal static class DestructiveRecovery
{
    public static async Task<DatabaseToolsResult> WrapAsync(
        DatabaseToolsIpcRequest request,
        Func<DatabaseToolsIpcRequest, CancellationToken, Task<DatabaseToolsResult>> dispatch,
        CancellationToken cancellationToken)
    {
        return request switch
        {
            UpgradeDatabaseIpcRequest u => await WrapUpgradeAsync(u.Request.DatabasePath, request, dispatch, cancellationToken),
            _ => await dispatch(request, cancellationToken)
        };
    }

    private static DatabaseToolsResult AppendToSummary(DatabaseToolsResult original, string? extra)
    {
        if (string.IsNullOrWhiteSpace(extra)) { return original; }

        var combined = string.IsNullOrWhiteSpace(original.FailureSummary)
            ? extra
            : $"{original.FailureSummary} | {extra}";

        return original with { FailureSummary = combined };
    }

    private static void TryDelete(string path)
    {
        if (!File.Exists(path)) { return; }

        try { File.Delete(path); }
        catch { /* best-effort; leftover .bak is acceptable */ }
    }

    private static async Task<DatabaseToolsResult> WrapUpgradeAsync(
        string targetPath,
        DatabaseToolsIpcRequest request,
        Func<DatabaseToolsIpcRequest, CancellationToken, Task<DatabaseToolsResult>> dispatch,
        CancellationToken cancellationToken)
    {
        var backupPath = targetPath + ".bak";

        if (File.Exists(backupPath))
        {
            return new DatabaseToolsResult(
                DatabaseToolsOutcome.Failed,
                $"A previous upgrade left an unresolved recovery backup at {backupPath}. Inspect / rename / delete it before retrying the upgrade so the recovery snapshot is not overwritten.",
                TimeSpan.Zero);
        }

        var backupCreated = false;

        if (File.Exists(targetPath))
        {
            try
            {
                File.Copy(targetPath, backupPath, overwrite: false);
                backupCreated = true;
            }
            catch (Exception ex)
            {
                return new DatabaseToolsResult(
                    DatabaseToolsOutcome.Failed,
                    $"Refused to start upgrade: pre-operation backup at {backupPath} failed ({ex.GetType().Name}: {ex.Message}). The original target was not modified.",
                    TimeSpan.Zero);
            }
        }

        var result = await dispatch(request, cancellationToken);

        if (result.Outcome == DatabaseToolsOutcome.Succeeded)
        {
            if (backupCreated) { TryDelete(backupPath); }

            return result;
        }

        if (!backupCreated || !File.Exists(backupPath))
        {
            return AppendToSummary(result, "No backup was created (target file did not exist before the upgrade attempt).");
        }

        try
        {
            File.Copy(backupPath, targetPath, overwrite: true);
            File.Delete(backupPath);

            return AppendToSummary(result, $"Original database restored from backup ({backupPath}).");
        }
        catch (Exception ex)
        {
            return AppendToSummary(result, $"Restore from backup failed: {ex.GetType().Name}: {ex.Message}. Backup remains at {backupPath} — rename manually to recover.");
        }
    }
}
