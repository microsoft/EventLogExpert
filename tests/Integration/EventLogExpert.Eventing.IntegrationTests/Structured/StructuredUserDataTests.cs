// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Interop;
using EventLogExpert.Eventing.Readers;
using EventLogExpert.Eventing.Structured;
using System.Xml;

namespace EventLogExpert.Eventing.IntegrationTests.Structured;

// Cross-checks the span-based schema discovery and value extraction against a System.Xml oracle on real rendered
// event XML. Gated on an env var pointing at a UserData-bearing .evtx (a real Microsoft-Windows-CAPI2 log) so hosts
// without the data skip cleanly rather than depending on a committed cert log.
public sealed class StructuredUserDataTests
{
    private const string Capi2EvtxEnvVar = "EVENTLOG_CAPI2_EVTX";

    [Fact]
    public void DiscoverAndCollect_OnRealUserData_AgreeWithSystemXml()
    {
        string? evtxPath = Environment.GetEnvironmentVariable(Capi2EvtxEnvVar);
        Assert.SkipUnless(
            !string.IsNullOrWhiteSpace(evtxPath) && File.Exists(evtxPath),
            $"Set {Capi2EvtxEnvVar} to a UserData-bearing .evtx to run the grounded discovery/extraction check.");

        string xml = FirstUserDataXml(evtxPath!);
        Assert.SkipUnless(xml.Length > 0, "No <UserData> event found in the file.");

        var discovered = StructuredSchemaDiscovery.DiscoverUserDataPaths([xml]);
        Assert.NotEmpty(discovered);
        Assert.All(discovered, path => Assert.StartsWith("Event/UserData/", path));

        string? scalarPath = discovered.FirstOrDefault(path => !StructuredFieldPath.IsWildcard(path) && path.Contains("/@", StringComparison.Ordinal));
        Assert.SkipUnless(scalarPath is not null, "No scalar UserData attribute discovered in the sample.");

        (string[] elements, string? attribute) = StructuredFieldPath.Parse(scalarPath!);
        StructuredFieldResult result = StructuredFieldPath.CollectValues(xml.AsSpan(), elements, attribute, StructuredFieldPath.MaxWildcardValues);

        Assert.Equal(OracleValues(xml, elements, attribute), result.Value.AsString());
    }

    private static string FirstUserDataXml(string evtxPath)
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

                    if (xml is not null && xml.Contains("<UserData", StringComparison.Ordinal)) { return xml; }
                }
            }
        }
        finally
        {
            query.Dispose();
        }

        return string.Empty;
    }

    // Independent System.Xml oracle: navigate the same element chain by local name and read the leaf value.
    private static string OracleValues(string xml, string[] elements, string? attribute)
    {
        string elementXPath = "/" + string.Join('/', elements.Select(name => $"*[local-name()='{name}']"));
        string xpath = attribute is null ? elementXPath : $"{elementXPath}/@*[local-name()='{attribute}']";

        var document = new XmlDocument();
        document.LoadXml(xml);

        XmlNodeList? nodes = document.SelectNodes(xpath);

        if (nodes is null) { return string.Empty; }

        return string.Join(", ", nodes.Cast<XmlNode>().Select(node => attribute is null ? node.InnerText : node.Value ?? string.Empty));
    }
}
