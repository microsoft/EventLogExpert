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
    public DatabaseImportSeverity Severity =>
        Failures.Count == 0 && UpgradeFailures.Count == 0 ? DatabaseImportSeverity.Info :
        Imported == 0 ? DatabaseImportSeverity.Error :
        DatabaseImportSeverity.Warning;
}
