// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Database;

namespace EventLogExpert.Runtime.Banner;

public enum DatabaseImportSeverity
{
    Info,
    Warning,
    Error
}

public abstract record DatabaseOperation
{
    public sealed record Toggle(string FileName) : DatabaseOperation;

    public sealed record Import : DatabaseOperation;

    public sealed record UpgradeSingle(string FileName) : DatabaseOperation;

    public sealed record UpgradeBatch(int Count) : DatabaseOperation;
}

public sealed record DatabaseRemoveFailed(string FileName, string Detail) : BannerMessage;

public sealed record DatabaseUpgradeFailed(string FileName, string Reason) : BannerMessage;

public sealed record DatabaseOperationFailed(DatabaseOperation Operation, string Detail) : BannerMessage;

public sealed record DatabaseImportSummary(
    int Imported,
    IReadOnlyList<ImportFailure> Failures,
    IReadOnlyList<ImportFailure> UpgradeFailures) : BannerMessage
{
    public IReadOnlyList<ImportFailure> Failures { get; } = Snapshot(Failures);

    public IReadOnlyList<ImportFailure> UpgradeFailures { get; } = Snapshot(UpgradeFailures);

    public DatabaseImportSeverity Severity =>
        Failures.Count == 0 && UpgradeFailures.Count == 0 ? DatabaseImportSeverity.Info :
        Imported == 0 ? DatabaseImportSeverity.Error :
        DatabaseImportSeverity.Warning;

    // Snapshot the caller-owned list so mutating it after the banner is queued cannot retroactively change the rendered
    // failure details or the computed Severity (banners are localized and rendered later). Get-only blocks the
    // with-based bypass.
    private static IReadOnlyList<ImportFailure> Snapshot(IReadOnlyList<ImportFailure> failures)
    {
        ArgumentNullException.ThrowIfNull(failures);

        return [.. failures];
    }
}
