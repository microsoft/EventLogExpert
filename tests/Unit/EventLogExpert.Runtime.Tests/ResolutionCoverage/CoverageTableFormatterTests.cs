// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.ResolutionCoverage;

namespace EventLogExpert.Runtime.Tests.ResolutionCoverage;

public sealed class CoverageTableFormatterTests
{
    [Fact]
    public void Format_ExceedingMaxCopyRows_TruncatesWithNote()
    {
        var rows = Enumerable.Range(0, CoverageTableFormatter.MaxCopyRows + 50)
            .Select(index => Row($"P{index:D6}", total: 1, noProvider: 1, status: CoverageStatus.None))
            .ToArray();

        string tsv = CoverageTableFormatter.Format(Report(rows), isFiltered: false);

        Assert.Contains("showing the first", tsv, StringComparison.Ordinal);

        // Scope comment + column header + exactly MaxCopyRows data rows.
        int lineCount = tsv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length;
        Assert.Equal(CoverageTableFormatter.MaxCopyRows + 2, lineCount);
    }

    [Fact]
    public void Format_FilteredView_NotesFilteredScope()
    {
        string tsv = CoverageTableFormatter.Format(
            Report(Row("A", total: 1, resolved: 1, status: CoverageStatus.Full)),
            isFiltered: true);

        Assert.Contains("(filtered view)", tsv, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_NeutralizesFormulaInjectionAndCollapsesTabs()
    {
        var report = Report(
            Row("=cmd|calc", total: 3, noProvider: 3, status: CoverageStatus.None),
            Row("Normal\tProvider", total: 2, resolved: 2, status: CoverageStatus.Full));

        string tsv = CoverageTableFormatter.Format(report, isFiltered: false);

        Assert.Contains("'=cmd|calc", tsv, StringComparison.Ordinal);
        Assert.Contains("Normal Provider", tsv, StringComparison.Ordinal);
        Assert.DoesNotContain("Normal\tProvider", tsv, StringComparison.Ordinal);
        Assert.Contains("All providers in the current view", tsv, StringComparison.Ordinal);
    }

    private static ResolutionCoverageReport Report(params ProviderCoverageRow[] rows)
    {
        ProviderResolutionCounts summary = default;

        foreach (var row in rows) { summary = summary.Add(row.Counts); }

        return new ResolutionCoverageReport(summary, rows);
    }

    private static ProviderCoverageRow Row(
        string provider,
        int total,
        int resolved = 0,
        int noProvider = 0,
        int noMessage = 0,
        int failed = 0,
        CoverageStatus status = CoverageStatus.Partial) =>
        new(provider, new ProviderResolutionCounts(total, resolved, noProvider, noMessage, failed), status);
}
