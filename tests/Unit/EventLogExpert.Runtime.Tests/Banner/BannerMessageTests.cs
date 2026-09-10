// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Banner;
using EventLogExpert.Runtime.Database;

namespace EventLogExpert.Runtime.Tests.Banner;

public sealed class BannerMessageTests
{
    [Fact]
    public void DatabaseImportSummary_SnapshotsFailures_SoLaterCallerMutationCannotChangeDetailsOrSeverity()
    {
        var failures = new List<ImportFailure> { new("A.db", new DatabaseFailureReason.NativeDetail("bad")) };
        var upgradeFailures = new List<ImportFailure> { new("B.db", new DatabaseFailureReason.NativeDetail("schema")) };
        var summary = new DatabaseImportSummary(2, failures, upgradeFailures);

        failures.Clear();
        upgradeFailures.Clear();

        Assert.Single(summary.Failures);
        Assert.Single(summary.UpgradeFailures);
        Assert.Equal(DatabaseImportSeverity.Warning, summary.Severity);
    }

    [Fact]
    public void EmptyLogs_CopiesDisplayNames_SoLaterCallerMutationCannotViolateInvariant()
    {
        var source = new List<string> { "Application.evtx", "System.evtx" };
        var message = new EmptyLogs(source);

        source.Clear();

        Assert.Equal(new[] { "Application.evtx", "System.evtx" }, message.DisplayNames);
    }

    [Fact]
    public void EmptyLogs_WhenDisplayNamesEmpty_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() => new EmptyLogs([]));

        Assert.Equal("displayNames", ex.ParamName);
    }

    [Fact]
    public void EmptyLogs_WhenDisplayNamesNull_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new EmptyLogs(null!));
    }
}
