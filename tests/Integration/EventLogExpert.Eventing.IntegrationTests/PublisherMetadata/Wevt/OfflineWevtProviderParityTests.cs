// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.PublisherMetadata;
using EventLogExpert.Eventing.PublisherMetadata.Wevt;
using EventLogExpert.Eventing.TestUtils.Constants;
using EventLogExpert.Provider.Resolution;
using System.Buffers;
using System.Xml;
using System.Xml.Linq;

namespace EventLogExpert.Eventing.IntegrationTests.PublisherMetadata.Wevt;

/// <summary>
///     Asserts that the offline WEVT parser produces the same resolved provider content as the native
///     publisher-metadata path for real providers: Security-Auditing supplies the rich event and template corpus,
///     Kernel-Power supplies the length-reference and FILETIME templates that exercise length / outType synthesis, and
///     PowerShell supplies the non-empty keyword, value-map, opcode, and task tables. Parity is checked by key and value
///     (not by count): the native enumeration may carry extra standard entries, so every offline entry must reproduce its
///     native counterpart exactly, while a partial decode regression is caught by comparing each offline table size to the
///     parsed post-dedup table count. Messages and parameters are excluded because they are resolved lazily, not by this
///     path; descriptions are compared, resolved eagerly through the shared assembler for both sources.
/// </summary>
public sealed class OfflineWevtProviderParityTests(
    OfflineWevtProviderParityTests.SecurityAuditingParityFixture securityAuditing,
    OfflineWevtProviderParityTests.PowerShellParityFixture powerShell,
    OfflineWevtProviderParityTests.KernelPowerParityFixture kernelPower)
    : IClassFixture<OfflineWevtProviderParityTests.SecurityAuditingParityFixture>,
        IClassFixture<OfflineWevtProviderParityTests.PowerShellParityFixture>,
        IClassFixture<OfflineWevtProviderParityTests.KernelPowerParityFixture>
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

            // Skip struct-bearing native templates: the offline reader fails those closed, so there is nothing to compare.
            if (nativeNodes is null || offlineNodes is null || TemplateHasStruct(nativeEvent.Template)) { continue; }

            // The structural compare includes the length and count attributes, so a length field-reference miss, an array
            // count miss, or a missing / wrong outType (the synthesizer always emits one) all fail here.
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

            // Skip struct-bearing native templates: the offline reader fails those closed, so there is nothing to compare.
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
    public void Events_SharedNonStructTemplates_OfflineSynthesizesWheneverNativeDoes()
    {
        // Closes a weak-gate blindspot: the structural parity tests skip events whose offline Template is empty, so a
        // partial fail-closed regression (some templates that should synthesize start returning "") would still pass as
        // long as one survivor matched. Here, over the shared (Id, Version) events whose native template is non-empty and
        // not a struct, the count of offline events that also synthesized a non-empty template must equal the native
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
            int nativeSynthesizable = 0;
            int offlineSynthesized = 0;

            foreach (((long Id, byte Version) key, EventModel nativeEvent) in nativeByKey)
            {
                if (string.IsNullOrEmpty(nativeEvent.Template) || TemplateHasStruct(nativeEvent.Template!)) { continue; }

                if (!offlineByKey.TryGetValue(key, out EventModel? offlineEvent)) { continue; }

                nativeSynthesizable++;

                if (!string.IsNullOrEmpty(offlineEvent.Template)) { offlineSynthesized++; }
            }

            Assert.True(nativeSynthesizable > 0, $"{label}: expected at least one shared non-struct native template to guard.");
            Assert.Equal(nativeSynthesizable, offlineSynthesized);
            comparedProviders++;
        }

        Assert.True(comparedProviders > 0, "Expected at least one parity provider to compare.");
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
    public void Opcodes_OfflineEntries_MatchNativeByKeyAndValue()
    {
        Assert.SkipUnless(powerShell.Available, SkipReasonFor(powerShell));

        // PowerShell defines many opcodes, exercising opcode decode: the raw OPCO id is already opcode << 16 and is passed
        // through unchanged so the assembler's (int)((uint)Value >> 16) projection recovers the native opcode key.
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

    public sealed class KernelPowerParityFixture() : ProviderParityFixture(Constants.KernelPowerLogName);

    public sealed class PowerShellParityFixture() : ProviderParityFixture(Constants.PowerShellLogName);

    public abstract class ProviderParityFixture
    {
        protected ProviderParityFixture(string providerName)
        {
            ProviderName = providerName;

            ProviderMetadata? metadata = ProviderMetadata.Create(providerName);

            if (metadata is null) { return; }

            Native = ProviderDetailsAssembler.Assemble(
                metadata.ToRawContent(providerName, logger: null), logger: null);

            string resourceFilePath = metadata.ResourceFilePath;

            if (string.IsNullOrEmpty(resourceFilePath)) { return; }

            Offline = OfflineWevtProviderReader.TryBuildProviderDetails(
                resourceFilePath,
                [metadata.MessageFilePath],
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

    private readonly record struct TemplateDataNode(string Name, string InType, string OutType, string Length, string Count);
}
