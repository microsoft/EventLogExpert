// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.PublisherMetadata;
using EventLogExpert.Eventing.PublisherMetadata.Wevt;
using EventLogExpert.Eventing.TestUtils.Constants;
using EventLogExpert.Provider.Resolution;
using EventLogExpert.ProviderDatabase.Hashing;
using System.Buffers;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace EventLogExpert.Eventing.IntegrationTests.PublisherMetadata.Wevt;

public sealed class OfflineWevtProviderParityTests(
    OfflineWevtProviderParityTests.SecurityAuditingParityFixture securityAuditing,
    OfflineWevtProviderParityTests.PowerShellParityFixture powerShell,
    OfflineWevtProviderParityTests.KernelPowerParityFixture kernelPower,
    OfflineWevtProviderParityTests.PerfOsParityFixture perfOs)
    : IClassFixture<OfflineWevtProviderParityTests.SecurityAuditingParityFixture>,
        IClassFixture<OfflineWevtProviderParityTests.PowerShellParityFixture>,
        IClassFixture<OfflineWevtProviderParityTests.KernelPowerParityFixture>,
        IClassFixture<OfflineWevtProviderParityTests.PerfOsParityFixture>
{
    [Fact]
    public void Descriptions_SharedByIdAndVersion_AreByteIdenticalToNative()
    {
        Assert.SkipUnless(securityAuditing.Available, SkipReasonFor(securityAuditing));

        Dictionary<(long Id, byte Version), EventModel> nativeByKey = BuildEventLookup(securityAuditing.Native!);
        int comparedCount = 0;

        foreach (EventModel offlineEvent in securityAuditing.Offline!.Events)
        {
            if (!nativeByKey.TryGetValue((offlineEvent.Id, offlineEvent.Version), out EventModel? nativeEvent) ||
                string.IsNullOrEmpty(nativeEvent.Description))
            {
                continue;
            }

            Assert.Equal(nativeEvent.Description, offlineEvent.Description);
            comparedCount++;
        }

        Assert.True(comparedCount > 0, "Expected at least one shared event with a non-empty native description to compare.");
    }

    [Fact]
    public void Events_KernelPowerTemplates_HaveLengthOutTypeAndArrayCountParity()
    {
        Assert.SkipUnless(kernelPower.Available, SkipReasonFor(kernelPower));

        Dictionary<(long Id, byte Version), EventModel> nativeByKey = BuildEventLookup(kernelPower.Native!);
        int comparedCount = 0;
        int lengthBearingNodes = 0;
        int fileTimeNodes = 0;
        int arrayBearingNodes = 0;

        foreach (EventModel offlineEvent in kernelPower.Offline!.Events)
        {
            if (string.IsNullOrEmpty(offlineEvent.Template)) { continue; }

            if (!nativeByKey.TryGetValue((offlineEvent.Id, offlineEvent.Version), out EventModel? nativeEvent) ||
                string.IsNullOrEmpty(nativeEvent.Template))
            {
                continue;
            }

            List<TemplateDataNode>? nativeNodes = ParseDataNodes(nativeEvent.Template);
            List<TemplateDataNode>? offlineNodes = ParseDataNodes(offlineEvent.Template);

            // Skip struct-bearing native templates here: ParseDataNodes compares only top-level <data> nodes, so structs
            // are covered by the dedicated Events_StructTemplates_CanonicalizeToNativeStructure test instead.
            if (nativeNodes is null || offlineNodes is null || TemplateHasStruct(nativeEvent.Template)) { continue; }

            // The structural compare includes the length and count attributes, so a length field-reference miss, an array
            // count miss, or a missing / wrong outType (the writer always emits one) all fail here.
            Assert.Equal(nativeNodes, offlineNodes);
            comparedCount++;
            lengthBearingNodes += offlineNodes.Count(static node => !string.IsNullOrEmpty(node.Length));
            fileTimeNodes += offlineNodes.Count(static node => node.InType == "win:FILETIME");
            arrayBearingNodes += offlineNodes.Count(static node => !string.IsNullOrEmpty(node.Count));
        }

        Assert.True(comparedCount > 0, "Expected at least one Kernel-Power non-struct template to compare.");

        // Kernel-Power is the corpus that proves length field-references (FIX 1) and always-emitted outType for FILETIME
        // (FIX 2): both must appear among the matched templates, otherwise the parity passed without exercising them. It
        // also carries array fields, so count="N" / count="<field>" reproduction is proven non-vacuously here too.
        Assert.True(lengthBearingNodes > 0, "Expected at least one matched length-bearing field (length field-reference parity).");
        Assert.True(fileTimeNodes > 0, "Expected at least one matched FILETIME field (always-emitted outType parity).");
        Assert.True(arrayBearingNodes > 0, "Expected at least one matched array field (count attribute parity).");
    }

    [Fact]
    public void Events_NonStructTemplates_HaveStructuralParity()
    {
        Assert.SkipUnless(securityAuditing.Available, SkipReasonFor(securityAuditing));

        Dictionary<(long Id, byte Version), EventModel> nativeByKey = BuildEventLookup(securityAuditing.Native!);
        int comparedCount = 0;

        foreach (EventModel offlineEvent in securityAuditing.Offline!.Events)
        {
            if (string.IsNullOrEmpty(offlineEvent.Template)) { continue; }

            if (!nativeByKey.TryGetValue((offlineEvent.Id, offlineEvent.Version), out EventModel? nativeEvent) ||
                string.IsNullOrEmpty(nativeEvent.Template))
            {
                continue;
            }

            List<TemplateDataNode>? nativeNodes = ParseDataNodes(nativeEvent.Template);
            List<TemplateDataNode>? offlineNodes = ParseDataNodes(offlineEvent.Template);

            // Skip struct-bearing native templates here: ParseDataNodes compares only top-level <data> nodes, so structs
            // are covered by the dedicated Events_StructTemplates_CanonicalizeToNativeStructure test instead.
            if (nativeNodes is null || offlineNodes is null || TemplateHasStruct(nativeEvent.Template)) { continue; }

            Assert.Equal(nativeNodes, offlineNodes);
            comparedCount++;
        }

        Assert.True(comparedCount > 0, "Expected at least one non-struct template to compare for structural parity.");
    }

    [Fact]
    public void Events_SharedByIdAndVersion_HaveFieldParity()
    {
        Assert.SkipUnless(securityAuditing.Available, SkipReasonFor(securityAuditing));

        Dictionary<(long Id, byte Version), EventModel> nativeByKey = BuildEventLookup(securityAuditing.Native!);
        int comparedCount = 0;

        foreach (EventModel offlineEvent in securityAuditing.Offline!.Events)
        {
            if (!nativeByKey.TryGetValue((offlineEvent.Id, offlineEvent.Version), out EventModel? nativeEvent))
            {
                continue;
            }

            Assert.Equal(nativeEvent.Level, offlineEvent.Level);
            Assert.Equal(nativeEvent.Opcode, offlineEvent.Opcode);
            Assert.Equal(nativeEvent.Task, offlineEvent.Task);
            Assert.Equal(nativeEvent.LogName, offlineEvent.LogName);
            Assert.Equal(nativeEvent.Keywords, offlineEvent.Keywords);
            comparedCount++;
        }

        Assert.True(comparedCount > 0, "Expected at least one event shared by id and version to compare for field parity.");
    }

    [Fact]
    public void Events_SharedNonStructTemplates_OfflineWritesWheneverNativeDoes()
    {
        // Closes a weak-gate blindspot: the structural parity tests skip events whose offline Template is empty, so a
        // partial fail-closed regression (some templates that should be written start returning "") would still pass as
        // long as one survivor matched. Here, over the shared (Id, Version) events whose native template is non-empty and
        // not a struct, the count of offline events that also wrote a non-empty template must equal the native
        // count - whenever native renders a non-struct template, offline must too. Native-only events are ignored so the
        // native superset is tolerated, matching the other parity tests.
        (ProviderParityFixture Fixture, string Label)[] corpus =
        [
            (securityAuditing, nameof(securityAuditing)),
            (kernelPower, nameof(kernelPower)),
            (powerShell, nameof(powerShell))
        ];

        Assert.SkipUnless(
            corpus.Any(static entry => entry.Fixture.Available),
            "Test requires at least one parity provider and its WEVT_TEMPLATE resource on the host.");

        int comparedProviders = 0;

        foreach ((ProviderParityFixture fixture, string label) in corpus)
        {
            if (!fixture.Available) { continue; }

            Dictionary<(long Id, byte Version), EventModel> nativeByKey = BuildEventLookup(fixture.Native!);
            Dictionary<(long Id, byte Version), EventModel> offlineByKey = BuildEventLookup(fixture.Offline!);
            int nativeWritable = 0;
            int offlineWritten = 0;

            foreach (((long Id, byte Version) key, EventModel nativeEvent) in nativeByKey)
            {
                if (string.IsNullOrEmpty(nativeEvent.Template) || TemplateHasStruct(nativeEvent.Template!)) { continue; }

                if (!offlineByKey.TryGetValue(key, out EventModel? offlineEvent)) { continue; }

                nativeWritable++;

                if (!string.IsNullOrEmpty(offlineEvent.Template)) { offlineWritten++; }
            }

            Assert.True(nativeWritable > 0, $"{label}: expected at least one shared non-struct native template to guard.");
            Assert.Equal(nativeWritable, offlineWritten);
            comparedProviders++;
        }

        Assert.True(comparedProviders > 0, "Expected at least one parity provider to compare.");
    }

    [Fact]
    public void Events_StructTemplates_CanonicalizeToNativeStructure()
    {
        string[] structProviders =
        [
            Constants.BitsClientLogName,
            Constants.Direct3D11LogName,
            Constants.DotNetRuntimeLogName,
            Constants.DwmCoreLogName
        ];

        int comparedProviders = 0;
        int comparedStructEvents = 0;

        foreach (string providerName in structProviders)
        {
            if (!TryLoadNativeAndOffline(providerName, out ProviderDetails? native, out ProviderDetails? offline))
            {
                continue;
            }

            Dictionary<(long Id, byte Version), EventModel> offlineByKey = BuildEventLookup(offline!);
            bool comparedThisProvider = false;

            foreach (EventModel nativeEvent in native!.Events)
            {
                if (string.IsNullOrEmpty(nativeEvent.Template) || !TemplateHasStruct(nativeEvent.Template))
                {
                    continue;
                }

                if (!offlineByKey.TryGetValue((nativeEvent.Id, nativeEvent.Version), out EventModel? offlineEvent))
                {
                    continue;
                }

                string? nativeShape = CanonicalizeTemplate(nativeEvent.Template);

                // Native always renders a parseable struct template here; if it somehow does not, there is nothing to gate.
                if (nativeShape is null) { continue; }

                // These four providers carry only non-nested structs, so offline must synthesize every struct event native
                // renders - a null/empty offline shape is a fail-closed regression, not an expected skip.
                string? offlineShape = CanonicalizeTemplate(offlineEvent.Template);
                Assert.True(
                    offlineShape is not null,
                    $"{providerName}: offline emitted an empty or unparseable template for struct event Id={nativeEvent.Id} V{nativeEvent.Version} that native renders.");

                // The canonical form preserves struct name / count / nesting and each data node's name/inType/outType/
                // length/count, so a struct-name, count-mode, member-type, or ordering regression fails here.
                Assert.Equal(nativeShape, offlineShape);
                comparedStructEvents++;
                comparedThisProvider = true;
            }

            if (comparedThisProvider) { comparedProviders++; }
        }

        Assert.SkipUnless(
            comparedProviders > 0,
            "Test requires at least one struct-bearing provider and its WEVT_TEMPLATE resource on the host.");

        Assert.True(comparedStructEvents > 0, "Expected at least one struct-bearing event to compare.");
    }

    [Fact]
    public void Keywords_OfflineEntries_MatchNativeByKeyAndValue()
    {
        Assert.SkipUnless(powerShell.Available, SkipReasonFor(powerShell));

        // PowerShell defines keywords, so this exercises keyword decode non-vacuously.
        Assert.NotEmpty(powerShell.Offline!.Keywords);

        // A partial decode regression is caught here: every parsed keyword (post-dedup by mask) must survive into the
        // resolved table, not just a non-empty subset of it.
        Assert.Equal(powerShell.RawKeywordKeyCount, powerShell.Offline!.Keywords.Count);

        AssertOfflineMatchesNative(powerShell.Native!.Keywords, powerShell.Offline!.Keywords);
    }

    [Fact]
    public void Maps_OfflineEntries_MatchNativeByKeyAndValue()
    {
        Assert.SkipUnless(powerShell.Available, SkipReasonFor(powerShell));

        // PowerShell defines value maps, so this exercises map decode non-vacuously.
        Assert.NotEmpty(powerShell.Offline!.Maps);

        Assert.Equal(powerShell.RawMapCount, powerShell.Offline!.Maps.Count);

        foreach ((string mapName, ValueMapDefinition offlineMap) in powerShell.Offline!.Maps)
        {
            Assert.True(powerShell.Native!.Maps.TryGetValue(mapName, out ValueMapDefinition? nativeMap),
                $"Native maps are missing the offline map '{mapName}'.");
            Assert.Equal(nativeMap!.IsBitMap, offlineMap.IsBitMap);
            Assert.Equal(nativeMap.Entries, offlineMap.Entries);
        }
    }

    [Fact]
    public void Messages_OfflineEntries_MatchNative()
    {
        Assert.SkipUnless(securityAuditing.Available, SkipReasonFor(securityAuditing));

        Assert.NotEmpty(securityAuditing.Native!.Messages);

        AssertMessagesEqual(securityAuditing.Native!.Messages, securityAuditing.Offline!.Messages);
    }

    [Fact]
    public void Opcodes_OfflineEntries_MatchNativeByKeyAndValue()
    {
        Assert.SkipUnless(powerShell.Available, SkipReasonFor(powerShell));

        // PowerShell defines many opcodes, exercising opcode decode: the raw OPCO id is already opcode << 16 and is passed
        // through unchanged so the factory's (int)((uint)Value >> 16) projection recovers the native opcode key.
        Assert.NotEmpty(powerShell.Offline!.Opcodes);

        Assert.Equal(powerShell.RawOpcodeKeyCount, powerShell.Offline!.Opcodes.Count);

        AssertOfflineMatchesNative(powerShell.Native!.Opcodes, powerShell.Offline!.Opcodes);
    }

    [Fact]
    public void Tasks_OfflineEntries_MatchNativeByKeyAndValue()
    {
        Assert.SkipUnless(powerShell.Available, SkipReasonFor(powerShell));

        Assert.NotEmpty(powerShell.Offline!.Tasks);

        Assert.Equal(powerShell.RawTaskKeyCount, powerShell.Offline!.Tasks.Count);

        AssertOfflineMatchesNative(powerShell.Native!.Tasks, powerShell.Offline!.Tasks);
    }

    [Fact]
    public void VersionKey_OfflineProvider_EqualsNative()
    {
        Assert.SkipUnless(securityAuditing.Available, SkipReasonFor(securityAuditing));

        Assert.Null(securityAuditing.Native!.ResolvedFromOwningPublisher);

        Assert.Equal(
            VersionKeyCalculator.Compute(securityAuditing.Native!),
            VersionKeyCalculator.Compute(securityAuditing.Offline!));
    }

    [Fact]
    public void VersionKey_PerfOsClassicParameterReferenceProvider_EqualsNative()
    {
        Assert.SkipUnless(perfOs.Available, SkipReasonFor(perfOs));

        Assert.Null(perfOs.Native!.ResolvedFromOwningPublisher);

        Assert.NotEmpty(perfOs.Offline!.Events);

        Assert.Equal(
            VersionKeyCalculator.Compute(perfOs.Native!),
            VersionKeyCalculator.Compute(perfOs.Offline!));
    }

    private static void AppendCanonicalElements(StringBuilder builder, IEnumerable<XElement> elements)
    {
        foreach (XElement element in elements)
        {
            switch (element.Name.LocalName)
            {
                case "struct":
                    builder.Append("<struct name=").Append(element.Attribute("name")?.Value)
                        .Append(" count=").Append(element.Attribute("count")?.Value).Append('>');
                    AppendCanonicalElements(builder, element.Elements());
                    builder.Append("</struct>");
                    break;
                case "data":
                    builder.Append("<data name=").Append(element.Attribute("name")?.Value)
                        .Append(" inType=").Append(element.Attribute("inType")?.Value)
                        .Append(" outType=").Append(element.Attribute("outType")?.Value)
                        .Append(" length=").Append(element.Attribute("length")?.Value)
                        .Append(" count=").Append(element.Attribute("count")?.Value).Append("/>");
                    break;
            }
        }
    }

    private static void AssertMessagesEqual(IReadOnlyList<MessageModel> native, IReadOnlyList<MessageModel> offline)
    {
        static MessageKey ToKey(MessageModel message) =>
            new(message.ShortId, message.RawId, message.LogLink, message.Tag, message.Template, message.Text);

        HashSet<MessageKey> nativeSet = native.Select(ToKey).ToHashSet();
        HashSet<MessageKey> offlineSet = offline.Select(ToKey).ToHashSet();

        Assert.True(
            nativeSet.SetEquals(offlineSet),
            $"Offline messages diverge from native: native {nativeSet.Count} distinct, offline {offlineSet.Count} distinct.");
    }

    private static void AssertOfflineMatchesNative<TKey>(IDictionary<TKey, string> native, IDictionary<TKey, string> offline)
        where TKey : notnull
    {
        // Parity is by key and value, tolerating native supersets (the native enumeration may carry standard entries the
        // provider binary omits). Every offline entry must reproduce its native counterpart exactly; callers assert
        // non-emptiness for providers known to define entries so a decode-to-empty regression fails rather than passing.
        foreach ((TKey key, string offlineValue) in offline)
        {
            Assert.True(native.TryGetValue(key, out string? nativeValue), $"Native result is missing offline key '{key}'.");
            Assert.Equal(nativeValue, offlineValue);
        }
    }

    private static Dictionary<(long Id, byte Version), EventModel> BuildEventLookup(ProviderDetails details)
    {
        Dictionary<(long Id, byte Version), EventModel> lookup = [];

        foreach (EventModel model in details.Events)
        {
            lookup.TryAdd((model.Id, model.Version), model);
        }

        return lookup;
    }

    private static string? CanonicalizeTemplate(string? template)
    {
        if (string.IsNullOrEmpty(template)) { return null; }

        XDocument document;

        try
        {
            document = XDocument.Parse(template);
        }
        catch (XmlException)
        {
            return null;
        }

        if (document.Root is null) { return null; }

        StringBuilder builder = new();
        AppendCanonicalElements(builder, document.Root.Elements());

        return builder.ToString();
    }

    private static List<TemplateDataNode>? ParseDataNodes(string template)
    {
        XDocument document;

        try
        {
            document = XDocument.Parse(template);
        }
        catch (XmlException)
        {
            return null;
        }

        if (document.Root is null) { return null; }

        return
        [
            .. document.Root.Elements()
                .Where(static element => element.Name.LocalName == "data")
                .Select(static element => new TemplateDataNode(
                    element.Attribute("name")?.Value ?? string.Empty,
                    element.Attribute("inType")?.Value ?? string.Empty,
                    element.Attribute("outType")?.Value ?? string.Empty,
                    element.Attribute("length")?.Value ?? string.Empty,
                    element.Attribute("count")?.Value ?? string.Empty))
        ];
    }

    private static string SkipReasonFor(ProviderParityFixture fixture) =>
        $"Test requires the {fixture.ProviderName} provider and its WEVT_TEMPLATE resource on the host.";

    private static bool TemplateHasStruct(string template)
    {
        XDocument document;

        try
        {
            document = XDocument.Parse(template);
        }
        catch (XmlException)
        {
            return false;
        }

        return document.Descendants().Any(static element => element.Name.LocalName == "struct");
    }

    private static bool TryLoadNativeAndOffline(string providerName, out ProviderDetails? native, out ProviderDetails? offline)
    {
        native = null;
        offline = null;

        ProviderMetadata? metadata = ProviderMetadata.Create(providerName);

        if (metadata is null || string.IsNullOrEmpty(metadata.ResourceFilePath))
        {
            return false;
        }

        native = new EventMessageProvider(providerName).LoadProviderDetails();
        offline = OfflineWevtProviderReader.TryBuildProviderDetails(
            metadata.ResourceFilePath,
            [metadata.MessageFilePath],
            metadata.ParameterFilePath,
            metadata.PublisherGuid,
            providerName,
            logger: null);

        return offline is not null;
    }

    public sealed class KernelPowerParityFixture() : ProviderParityFixture(Constants.KernelPowerLogName);

    public sealed class PerfOsParityFixture() : ProviderParityFixture(Constants.PerfOsLogName);

    public sealed class PowerShellParityFixture() : ProviderParityFixture(Constants.PowerShellLogName);

    public abstract class ProviderParityFixture
    {
        protected ProviderParityFixture(string providerName)
        {
            ProviderName = providerName;

            ProviderMetadata? metadata = ProviderMetadata.Create(providerName);

            if (metadata is null) { return; }

            Native = new EventMessageProvider(providerName, logger: null).LoadProviderDetails();

            string resourceFilePath = metadata.ResourceFilePath;

            if (string.IsNullOrEmpty(resourceFilePath)) { return; }

            Offline = OfflineWevtProviderReader.TryBuildProviderDetails(
                resourceFilePath,
                [metadata.MessageFilePath],
                metadata.ParameterFilePath,
                metadata.PublisherGuid,
                providerName,
                logger: null);

            CaptureRawTableCounts(resourceFilePath, metadata.PublisherGuid);
        }

        public bool Available => Native is not null && Offline is not null;

        public ProviderDetails? Native { get; }

        public ProviderDetails? Offline { get; }

        public string ProviderName { get; }

        public int RawKeywordKeyCount { get; private set; }

        public int RawMapCount { get; private set; }

        public int RawOpcodeKeyCount { get; private set; }

        public int RawTaskKeyCount { get; private set; }

        private void CaptureRawTableCounts(string resourceFilePath, Guid publisherGuid)
        {
            // Re-parse the resource to record the post-dedup table sizes the resolved provider details must reproduce, so
            // a partial decode regression (some entries dropped between parse and assembly) is detectable by count.
            byte[]? rented = WevtTemplateReader.TryRentWevtResource(resourceFilePath, logger: null, out int size);

            if (rented is null) { return; }

            try
            {
                WevtProviderData? raw = WevtTemplateReader.TryParseProvider(
                    rented.AsSpan(0, size), publisherGuid, logger: null);

                if (raw is null) { return; }

                RawKeywordKeyCount = raw.Keywords.Select(static keyword => keyword.Mask).Distinct().Count();
                RawMapCount = raw.Templates.Maps.Count;
                RawOpcodeKeyCount = raw.Opcodes.Select(static opcode => opcode.Id).Distinct().Count();
                RawTaskKeyCount = raw.Tasks.Select(static task => task.Id).Distinct().Count();
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }

    public sealed class SecurityAuditingParityFixture() : ProviderParityFixture(Constants.SecurityAuditingLogName);

    private readonly record struct MessageKey(short ShortId, long RawId, string? LogLink, string? Tag, string? Template, string Text);

    private readonly record struct TemplateDataNode(string Name, string InType, string OutType, string Length, string Count);
}
