// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.IntegrationTests.Readers;
using EventLogExpert.Eventing.Readers;
using EventLogExpert.Eventing.Resolvers;

namespace EventLogExpert.Eventing.IntegrationTests.Resolvers;

public sealed class EventXmlBatchScannerTests
{
    [Fact]
    public void Scan_WhenBatchSizeForcesMultiplePasses_YieldsEveryEventExactlyOnce()
    {
        using SmallEvtxFixture fixture = new();
        (_, List<long> oracleRecordIds) = ReadOracle(fixture.FilePath);
        Assert.True(oracleRecordIds.Count > 1, "fixture must export multiple events to exercise batch boundaries");

        // A batch size of 1 forces one EvtNext pass per event plus the trailing ERROR_NO_MORE_ITEMS pass.
        EventXmlBatchScanner scanner = new(batchSize: 1);

        List<long> scannedRecordIds =
        [
            .. scanner
                .Scan(fixture.FilePath, LogPathType.File, static _ => true, TestContext.Current.CancellationToken)
                .Select(scanned => scanned.RecordId)
        ];

        Assert.Equal(oracleRecordIds.OrderBy(id => id), scannedRecordIds.OrderBy(id => id));
        Assert.Equal(scannedRecordIds.Count, scannedRecordIds.Distinct().Count());
    }

    [Fact]
    public void Scan_WhenCancellationRequested_ThrowsOperationCanceledException()
    {
        using SmallEvtxFixture fixture = new();
        EventXmlBatchScanner scanner = new();
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() =>
            scanner.Scan(fixture.FilePath, LogPathType.File, static _ => true, cancellation.Token).ToList());
    }

    [Fact]
    public void Scan_WhenLogPathDoesNotExist_ThrowsInsteadOfReturningEmpty()
    {
        string missingPath = Path.Combine(Path.GetTempPath(), $"elx-missing-{Guid.NewGuid():N}.evtx");
        EventXmlBatchScanner scanner = new();

        Assert.Throws<FileNotFoundException>(() =>
            scanner.Scan(missingPath, LogPathType.File, static _ => true, TestContext.Current.CancellationToken).ToList());
    }

    [Fact]
    public void Scan_WhenOwningLogEmpty_ThrowsArgumentExceptionEagerly()
    {
        EventXmlBatchScanner scanner = new();

        // Eager: the argument guard must fire on the call, not be deferred to the first enumeration step.
        Assert.Throws<ArgumentException>(() =>
            scanner.Scan(string.Empty, LogPathType.File, static _ => true, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Scan_WhenPredicateAcceptsAllRecords_YieldsEveryEventWithMatchingRecordIdAndXml()
    {
        using SmallEvtxFixture fixture = new();
        (Dictionary<long, string> oracleXml, List<long> oracleRecordIds) = ReadOracle(fixture.FilePath);
        EventXmlBatchScanner scanner = new();

        List<ScannedEventXml> results =
        [
            .. scanner
                .Scan(fixture.FilePath, LogPathType.File, static _ => true, TestContext.Current.CancellationToken)
        ];

        Assert.NotEmpty(results);
        Assert.Equal(oracleRecordIds.OrderBy(id => id), results.Select(scanned => scanned.RecordId).OrderBy(id => id));

        foreach (ScannedEventXml scanned in results)
        {
            Assert.Equal(oracleXml[scanned.RecordId], scanned.Xml);
            Assert.Contains("<EventRecordID>", scanned.Xml, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Scan_WhenPredicateRejectsAllRecords_YieldsNothingAndCompletes()
    {
        using SmallEvtxFixture fixture = new();
        EventXmlBatchScanner scanner = new();

        List<ScannedEventXml> results =
        [
            .. scanner
                .Scan(fixture.FilePath, LogPathType.File, static _ => false, TestContext.Current.CancellationToken)
        ];

        Assert.Empty(results);
    }

    [Fact]
    public void Scan_WhenPredicateSelectsSingleRecord_YieldsOnlyThatRecordWithItsXml()
    {
        using SmallEvtxFixture fixture = new();
        (Dictionary<long, string> oracleXml, List<long> oracleRecordIds) = ReadOracle(fixture.FilePath);
        long target = oracleRecordIds[0];
        EventXmlBatchScanner scanner = new();

        List<ScannedEventXml> results =
        [
            .. scanner
                .Scan(fixture.FilePath,
                    LogPathType.File,
                    recordId => recordId == target,
                    TestContext.Current.CancellationToken)
        ];

        ScannedEventXml scanned = Assert.Single(results);
        Assert.Equal(target, scanned.RecordId);
        Assert.Equal(oracleXml[target], scanned.Xml);
    }

    [Fact]
    public void Scan_WhenShouldRenderXmlNull_ThrowsArgumentNullExceptionEagerly()
    {
        EventXmlBatchScanner scanner = new();

        Assert.Throws<ArgumentNullException>(() =>
            scanner.Scan("Application", LogPathType.Channel, null!, TestContext.Current.CancellationToken));
    }

    private static (Dictionary<long, string> XmlByRecordId, List<long> RecordIds) ReadOracle(string filePath)
    {
        Dictionary<long, string> xmlByRecordId = new();
        List<long> recordIds = [];

        using EventLogReader reader = new(filePath, LogPathType.File, renderXml: true);

        while (reader.TryGetEvents(out EventRecord[] batch) && batch.Length > 0)
        {
            foreach (EventRecord record in batch)
            {
                if (record.RecordId is not { } recordId) { continue; }

                recordIds.Add(recordId);
                xmlByRecordId[recordId] = record.Xml ?? string.Empty;
            }
        }

        return (xmlByRecordId, recordIds);
    }
}
