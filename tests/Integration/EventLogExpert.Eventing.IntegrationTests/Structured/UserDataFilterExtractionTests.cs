// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Eventing.Interop;
using EventLogExpert.Eventing.Readers;
using EventLogExpert.Eventing.Structured;
using System.Xml;

namespace EventLogExpert.Eventing.IntegrationTests.Structured;

// Proves the resolve-time UserData extraction pipeline end to end on real Microsoft-Windows-CAPI2 events: the
// EventLogReader gates on the empty User render context, span-scans each nested-UserData event, and stores its deduped
// values on EventRecord.UserData. A filter reads them back by storage key via TryGetUserDataValues, and every value is
// cross-checked against an independent System.Xml oracle. Gated on a UserData-bearing .evtx so hosts without it skip.
public sealed class UserDataFilterExtractionTests
{
    private const string Capi2EvtxEnvVar = "EVENTLOG_CAPI2_EVTX";

    [Fact]
    public void Reader_ExtractsUserDataLeaves_MatchingAnOracle()
    {
        string? evtxPath = Environment.GetEnvironmentVariable(Capi2EvtxEnvVar);
        Assert.SkipUnless(
            !string.IsNullOrWhiteSpace(evtxPath) && File.Exists(evtxPath),
            $"Set {Capi2EvtxEnvVar} to a UserData-bearing .evtx to run the grounded filter-extraction check.");

        string sampleXml = FirstNestedUserDataXml(evtxPath!);
        Assert.SkipUnless(sampleXml.Length > 0, "No nested <UserData> event found in the file.");

        var discovered = StructuredSchemaDiscovery.DiscoverUserDataPaths([sampleXml]);

        string? scalarPath = discovered.FirstOrDefault(path =>
            !StructuredFieldPath.IsWildcard(path) && path.Contains("/@", StringComparison.Ordinal));

        Assert.SkipUnless(scalarPath is not null, "No scalar UserData attribute discovered in the sample.");

        (string[] elements, string? attribute) = StructuredFieldPath.Parse(scalarPath!);

        string? targetValue = OracleValues(sampleXml, elements, attribute).FirstOrDefault(value => value.Length > 0);
        Assert.SkipUnless(targetValue is not null, "The discovered scalar attribute had no value to target.");

        // The reader stores each value under its storage key, so ToStorageKey(discoveredPath) is how the stored field is
        // keyed; == and != are re-evaluated from the stored result and cross-checked against the oracle for the same path.
        string storageKey = StructuredFieldPath.ToStorageKey(scalarPath!);

        using var reader = new EventLogReader(evtxPath!, LogPathType.File, renderXml: true);
        Assert.True(reader.IsValid, $"EventLogReader could not open '{evtxPath}'.");

        int equalMatches = 0;
        int checkedEvents = 0;
        bool anyUserDataPopulated = false;

        while (reader.TryGetEvents(out var events))
        {
            foreach (var record in events)
            {
                // Only nested-UserData events (empty User render context) are extraction-gated, as in production; flat
                // UserData surfaces as EventData and is not stored here.
                if (!record.IsSuccess || record.Xml is null || record.Properties.Length != 0) { continue; }

                var oracleValues = OracleValues(record.Xml, elements, attribute);
                bool present = oracleValues.Count > 0;
                bool anyEqual = oracleValues.Contains(targetValue!, StringComparer.Ordinal);

                // Scalar attribute: single-valued, never truncated, so no Unknown is expected here.
                FilterMatch expectedEqual = anyEqual ? FilterMatch.Match : FilterMatch.NoMatch;
                FilterMatch expectedNotEqual = present && !anyEqual ? FilterMatch.Match : FilterMatch.NoMatch;

                // The reader stamps EventRecord.UserData; the resolver copies it onto ResolvedEvent for a compiled filter.
                // Reconstruct that lookup surface from the raw record to exercise the same TryGetUserDataValues path.
                var resolved = new ResolvedEvent("UserDataFilterExtractionTests", LogPathType.File)
                {
                    UserData = record.UserData,
                    UserDataIncomplete = record.UserDataIncomplete
                };

                var result = resolved.TryGetUserDataValues(storageKey);

                if (!record.UserData.IsDefaultOrEmpty) { anyUserDataPopulated = true; }

                // The stored field's values equal the independent oracle's values for the same path.
                Assert.Equal(oracleValues, result.PresentValues.ToArray());

                Assert.Equal(expectedEqual, EqualsAny(result, targetValue!));
                Assert.Equal(expectedNotEqual, NotEqualsPresent(result, targetValue!));

                if (expectedEqual == FilterMatch.Match) { equalMatches++; }

                checkedEvents++;
            }
        }

        Assert.True(checkedEvents > 0, "No nested-UserData events were read from the file.");
        Assert.True(anyUserDataPopulated, "The reader never populated stored UserData leaves.");
        Assert.True(equalMatches > 0, "No event matched the target UserData value the sample was seeded from.");
    }

    private static FilterMatch EqualsAny(StructuredFieldResult result, string target)
    {
        var values = result.PresentValues;

        for (int index = 0; index < values.Length; index++)
        {
            if (string.Equals(values[index], target, StringComparison.Ordinal)) { return FilterMatch.Match; }
        }

        return result.IsTruncated ? FilterMatch.Unknown : FilterMatch.NoMatch;
    }

    private static string FirstNestedUserDataXml(string evtxPath)
    {
        EvtHandle query = NativeMethods.EvtQuery(EventLogSession.GlobalSession.Handle, evtxPath, null, LogPathType.File);
        Assert.False(query.IsInvalid, $"EvtQuery failed for file '{evtxPath}'.");

        try
        {
            for (int scanned = 0; scanned < 2000;)
            {
                var batch = new IntPtr[16];
                int returned = 0;

                if (!NativeMethods.EvtNext(query, batch.Length, batch, 0, 0, ref returned) || returned == 0) { break; }

                for (int index = 0; index < returned; index++)
                {
                    using var handle = new EvtHandle(batch[index]);
                    scanned++;

                    string? xml = NativeMethods.RenderEventXml(handle);

                    if (xml is not null
                        && xml.Contains("<UserData", StringComparison.Ordinal)
                        && NativeMethods.RenderEventProperties(handle).Length == 0)
                    {
                        for (int remaining = index + 1; remaining < returned; remaining++)
                        {
                            new EvtHandle(batch[remaining]).Dispose();
                        }

                        return xml;
                    }
                }
            }
        }
        finally
        {
            query.Dispose();
        }

        return string.Empty;
    }

    private static FilterMatch NotEqualsPresent(StructuredFieldResult result, string target)
    {
        var values = result.PresentValues;

        if (values.Length == 0) { return FilterMatch.NoMatch; }

        for (int index = 0; index < values.Length; index++)
        {
            if (string.Equals(values[index], target, StringComparison.Ordinal)) { return FilterMatch.NoMatch; }
        }

        return result.IsTruncated ? FilterMatch.Unknown : FilterMatch.Match;
    }

    // Independent System.Xml oracle: navigate the same element chain by local name and read each value.
    private static List<string> OracleValues(string xml, string[] elements, string? attribute)
    {
        string elementXPath = "/" + string.Join('/', elements.Select(name => $"*[local-name()='{name}']"));
        string xpath = attribute is null ? elementXPath : $"{elementXPath}/@*[local-name()='{attribute}']";

        var document = new XmlDocument();
        document.LoadXml(xml);

        XmlNodeList? nodes = document.SelectNodes(xpath);
        var values = new List<string>();

        if (nodes is null) { return values; }

        foreach (XmlNode node in nodes)
        {
            values.Add(attribute is null ? node.InnerText : node.Value ?? string.Empty);
        }

        return values;
    }
}
