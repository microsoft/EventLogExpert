// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Eventing.Interop;
using EventLogExpert.Eventing.Readers;
using EventLogExpert.Eventing.TestUtils.Constants;
using System.Xml;

namespace EventLogExpert.Eventing.IntegrationTests.Interop;

public sealed class ValuePathsRenderTests
{
    private const string Capi2EvtxEnvVar = "EVENTLOG_CAPI2_EVTX";

    [Fact]
    public void RenderEventValues_ForAllAbsentPaths_YieldsNullsWithoutThrowing()
    {
        using EvtHandle handle = FirstEventHandle(Constants.ApplicationLogName, LogPathType.Channel);
        Assert.SkipUnless(!handle.IsInvalid, "Application log has no readable events on this host.");

        string[] paths =
        [
            "Event/System/NoSuchElementOne",
            "Event/UserData/NoSuchProviderRoot/NoSuchLeaf",
            "Event/EventData/Data[@Name='__no_such_field__']"
        ];

        var values = RenderValues(paths, handle);

        Assert.Equal(paths.Length, values.Length);
        Assert.All(values, value => Assert.Equal(EventFieldValueKind.Null, value.Kind));
    }

    // Grounded check against a real Microsoft-Windows-CAPI2 log, gated on an env var so hosts without the data skip.
    // Proves the value-paths engine and a local-name System.Xml parse agree on a real UserData attribute whose
    // element chain carries no explicit xmlns and inherits the event default namespace.
    [Fact]
    public void RenderEventValues_ForRealUserDataAttribute_MatchesLocalNameXmlParse()
    {
        string? evtxPath = Environment.GetEnvironmentVariable(Capi2EvtxEnvVar);
        Assert.SkipUnless(
            !string.IsNullOrWhiteSpace(evtxPath) && File.Exists(evtxPath),
            $"Set {Capi2EvtxEnvVar} to a UserData-bearing .evtx to run the grounded CAPI2 parity check.");

        EvtHandle query = NativeMethods.EvtQuery(EventLogSession.GlobalSession.Handle, evtxPath!, null, LogPathType.File);
        Assert.False(query.IsInvalid, $"EvtQuery failed for file '{evtxPath}'.");

        try
        {
            (EvtHandle handle, XmlElement root, string userDataPath, string expected) = FirstUserDataAttribute(query);

            using (handle)
            {
                Assert.SkipUnless(!handle.IsInvalid, "No <UserData> event with a value-bearing attribute was found in the file.");

                string absentPath = SiblingAbsentPath(userDataPath);
                string[] paths = [userDataPath, absentPath];

                var values = RenderValues(paths, handle);

                Assert.Equal(2, values.Length);
                Assert.Equal(expected, values[0].AsString());               // value-paths == local-name XML parse
                Assert.Equal(EventFieldValueKind.Null, values[1].Kind);     // absent sibling -> Null
            }
        }
        finally
        {
            query.Dispose();
        }
    }

    // Test A - mechanical, provider-agnostic. Well-known System value-paths are present on every event, so this runs
    // on any host with a readable Application log and validates the whole native pipeline plus the namespace behavior
    // (unqualified value-paths resolving against the event default namespace) without any external data.
    [Fact]
    public void RenderEventValues_ForSystemPaths_MatchesXmlAndYieldsNullForAbsent()
    {
        using EvtHandle handle = FirstEventHandle(Constants.ApplicationLogName, LogPathType.Channel);
        Assert.SkipUnless(!handle.IsInvalid, "Application log has no readable events on this host.");

        string xml = NativeMethods.RenderEventXml(handle) ?? throw new InvalidOperationException("RenderEventXml returned null.");
        XmlElement root = LoadEventXml(xml);

        string[] paths =
        [
            "Event/System/EventID",
            "Event/System/Provider/@Name",
            "Event/System/Computer",
            "Event/System/ThisElementDoesNotExist"   // absent -> must render as Null, not throw
        ];

        var values = RenderValues(paths, handle);

        // The whole path array marshalled and the render is index-aligned to it.
        Assert.Equal(paths.Length, values.Length);

        // Present paths agree with the local-name XML parse.
        Assert.Equal(LocalNameText(root, "EventID"), values[0].AsString());
        Assert.Equal(LocalNameAttribute(root, "Provider", "Name"), values[1].AsString());
        Assert.Equal(LocalNameText(root, "Computer"), values[2].AsString());

        // Absent path -> Null; the common absent case must not throw.
        Assert.Equal(EventFieldValueKind.Null, values[3].Kind);
    }

    // Builds an Event-rooted value-path of element local names down to @attribute, e.g.
    // Event/UserData/CertVerifyCertificateChainPolicy/EventAuxInfo/@ProcessName.
    private static string BuildValuePath(XmlElement leaf, string attributeLocalName)
    {
        var names = new List<string>();

        for (XmlNode? node = leaf; node is XmlElement element; node = node.ParentNode)
        {
            names.Insert(0, element.LocalName);
        }

        return string.Join('/', names) + "/@" + attributeLocalName;
    }

    private static IEnumerable<XmlNode> EnumerateElements(XmlNode node)
    {
        foreach (XmlNode child in node.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element) { continue; }

            yield return child;

            foreach (XmlNode nested in EnumerateElements(child)) { yield return nested; }
        }
    }

    private static EvtHandle FirstEventHandle(string path, LogPathType pathType)
    {
        EvtHandle query = NativeMethods.EvtQuery(EventLogSession.GlobalSession.Handle, path, null, pathType);
        Assert.False(query.IsInvalid, $"EvtQuery failed for '{path}'.");

        try
        {
            var batch = new IntPtr[1];
            int returned = 0;

            if (!NativeMethods.EvtNext(query, batch.Length, batch, 0, 0, ref returned) || returned != 1)
            {
                return EvtHandle.Zero;
            }

            return new EvtHandle(batch[0]);
        }
        finally
        {
            query.Dispose();
        }
    }

    private static XmlNode? FirstLocalName(XmlNode node, string localName)
    {
        foreach (XmlNode element in EnumerateElements(node))
        {
            if (element.LocalName == localName) { return element; }
        }

        return null;
    }

    // Scans events until it finds one whose <UserData> subtree has an attribute with a non-empty value, then returns
    // that event handle, its parsed XML, the canonical local-name value-path to the attribute, and the expected value.
    private static (EvtHandle Handle, XmlElement Root, string Path, string Expected) FirstUserDataAttribute(EvtHandle query)
    {
        const int scanLimit = 2000;
        int scanned = 0;

        while (scanned < scanLimit)
        {
            var batch = new IntPtr[16];
            int returned = 0;

            if (!NativeMethods.EvtNext(query, batch.Length, batch, 0, 0, ref returned) || returned == 0) { break; }

            for (int i = 0; i < returned; i++)
            {
                var handle = new EvtHandle(batch[i]);
                scanned++;

                string? xml = NativeMethods.RenderEventXml(handle);

                if (xml is not null && TryFindUserDataAttribute(xml, out XmlElement root, out string path, out string expected))
                {
                    // Dispose the other handles in this batch; keep the match.
                    for (int j = i + 1; j < returned; j++) { new EvtHandle(batch[j]).Dispose(); }

                    return (handle, root, path, expected);
                }

                handle.Dispose();
            }
        }

        return (EvtHandle.Zero, null!, string.Empty, string.Empty);
    }

    private static XmlElement LoadEventXml(string xml)
    {
        var document = new XmlDocument();
        document.LoadXml(xml);

        return document.DocumentElement ?? throw new InvalidOperationException("Event XML has no root element.");
    }

    private static string LocalNameAttribute(XmlElement root, string elementLocalName, string attributeLocalName)
    {
        if (FirstLocalName(root, elementLocalName) is not XmlElement element) { return string.Empty; }

        foreach (XmlAttribute attribute in element.Attributes)
        {
            if (attribute.LocalName == attributeLocalName) { return attribute.Value; }
        }

        return string.Empty;
    }

    private static string LocalNameText(XmlElement root, string localName) =>
        FirstLocalName(root, localName)?.InnerText ?? string.Empty;

    private static EventFieldValue[] RenderValues(string[] paths, EvtHandle handle)
    {
        using EvtHandle context = NativeMethods.EvtCreateRenderContext(paths.Length, paths, EvtRenderContextFlags.Values);
        Assert.False(context.IsInvalid, "EvtCreateRenderContext(Values) failed.");

        var rendered = NativeMethods.RenderEventValues(context, handle);

        var values = new EventFieldValue[rendered.Length];
        for (int i = 0; i < rendered.Length; i++) { values[i] = EventFieldValue.FromProperty(rendered[i]); }

        return values;
    }

    private static string SiblingAbsentPath(string valuePath)
    {
        int lastSlash = valuePath.LastIndexOf('/');

        return valuePath[..(lastSlash + 1)] + "@__no_such_attribute__";
    }

    private static bool TryFindUserDataAttribute(string xml, out XmlElement root, out string path, out string expected)
    {
        root = LoadEventXml(xml);
        path = string.Empty;
        expected = string.Empty;

        XmlNode? userData = FirstLocalName(root, "UserData");
        if (userData is null) { return false; }

        foreach (XmlNode descendant in EnumerateElements(userData))
        {
            if (descendant.Attributes is null) { continue; }

            foreach (XmlAttribute attribute in descendant.Attributes)
            {
                if (attribute.Name.StartsWith("xmlns", StringComparison.Ordinal)) { continue; }
                if (string.IsNullOrEmpty(attribute.Value)) { continue; }

                path = BuildValuePath((XmlElement)descendant, attribute.LocalName);
                expected = attribute.Value;

                return true;
            }
        }

        return false;
    }
}
