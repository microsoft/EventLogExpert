// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Interop;
using EventLogExpert.Logging.Abstractions;
using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace EventLogExpert.Eventing.PublisherMetadata.Wevt;

internal readonly record struct WevtRawMapEntry(uint Value, uint MessageId);

internal sealed record WevtRawMap(bool IsBitMap, IReadOnlyList<WevtRawMapEntry> Entries);

internal readonly record struct WevtEventKey(uint Id, byte Version);

internal sealed class WevtTemplateData
{
    public required IReadOnlyDictionary<WevtEventKey, IReadOnlyDictionary<string, string>> EventFieldMaps { get; init; }

    public required IReadOnlyDictionary<string, WevtRawMap> Maps { get; init; }
}

/// <summary>
///     A single flat template field descriptor used to synthesize the manifest template XML. <see cref="Flags" /> is
///     the descriptor's flags@0 word (carries the length-reference bit); <see cref="LengthRefIndex" /> is the length@14
///     field, the 0-based index of the length-providing item when the length-reference bit is set.
/// </summary>
internal readonly record struct WevtTemplateItem(
    string Name,
    byte InType,
    byte OutType,
    ushort Count,
    uint Flags,
    ushort LengthRefIndex);

/// <summary>
///     A parsed event template. <see cref="IsStruct" /> is set when the template carries nested struct data (more
///     name entries than descriptor items); such templates fail closed (no flat synthesis) until P2d adds struct support.
/// </summary>
internal sealed class WevtParsedTemplate
{
    public required bool IsStruct { get; init; }

    public required IReadOnlyList<WevtTemplateItem> Items { get; init; }
}

/// <summary>A provider event read in full from the EVNT table, before any name/keyword resolution.</summary>
internal sealed record WevtProviderEvent(
    uint Id,
    byte Version,
    byte Channel,
    byte Level,
    byte Opcode,
    ushort Task,
    ulong Keywords,
    uint MessageId,
    WevtParsedTemplate? Template);

/// <summary>A CHAN table row. <see cref="ReferenceId" /> (the @8 aux field) is what an event's channel byte references.</summary>
internal readonly record struct WevtChannelEntry(uint Id, uint ReferenceId, uint MessageId, string? InlineName);

/// <summary>A LEVL / OPCO / TASK table row keyed by its numeric id.</summary>
internal readonly record struct WevtIdentifiedEntry(uint Id, uint MessageId, string? InlineName);

/// <summary>A KEYW table row keyed by its 64-bit bit mask.</summary>
internal readonly record struct WevtKeywordEntry(ulong Mask, uint MessageId, string? InlineName);

/// <summary>
///     The full set of tables parsed from one provider's WEVT_TEMPLATE in a single pass: the value-map data the
///     shipped map API exposes plus the events / channels / levels / opcodes / tasks / keywords the offline provider
///     reader maps to <see cref="RawProviderContent" />.
/// </summary>
internal sealed class WevtProviderData
{
    public required IReadOnlyList<WevtChannelEntry> Channels { get; init; }

    public required IReadOnlyList<WevtProviderEvent> Events { get; init; }

    public required IReadOnlyList<WevtKeywordEntry> Keywords { get; init; }

    public required IReadOnlyList<WevtIdentifiedEntry> Levels { get; init; }

    public required IReadOnlyList<WevtIdentifiedEntry> Opcodes { get; init; }

    public required IReadOnlyList<WevtIdentifiedEntry> Tasks { get; init; }

    public required WevtTemplateData Templates { get; init; }
}

/// <summary>
///     Reads the binary WEVT_TEMPLATE resource embedded in a provider DLL. A single pass extracts the value-map /
///     bitMap definitions and per-event field-to-map associations (the shipped map API) as well as the full event,
///     channel, level, opcode, task, and keyword tables the offline provider reader consumes.
/// </summary>
/// <remarks>
///     EvtOpenPublisherMetadata exposes no valueMap / bitMap tables and strips the <c>map</c> attribute from the
///     template XML, so the decoded names (for example a bus type of <c>10</c> shown as <c>SAS</c>) are recovered by
///     parsing the compiled resource directly. Every offset is bounds-checked against the actual resource size; a
///     malformed resource yields <c>null</c> for the map API and an empty table for the full parse.
/// </remarks>
internal static class WevtTemplateReader
{
    private const string BmapSignature = "BMAP";
    private const int ChannelAuxOffset = 8;
    private const int ChannelEntrySize = 16;
    private const int ChannelMessageIdOffset = 12;
    private const int ChannelNameDataOffset = 4;
    private const string ChanSignature = "CHAN";
    private const int CrimProviderCountOffset = 12;
    private const int CrimProviderDescriptorArrayOffset = 16;
    private const int CrimProviderDescriptorSize = 20;
    private const string CrimSignature = "CRIM";
    private const int EventChannelOffset = 3;
    private const int EventDefinitionSize = 48;
    private const int EventDefinitionTemplateOffset = 20;
    private const int EventDefinitionVersionOffset = 2;
    private const int EventKeywordsOffset = 8;
    private const int EventLevelOffset = 4;
    private const int EventMessageIdOffset = 16;
    private const int EventOpcodeOffset = 5;
    private const int EventTableArrayOffset = 16;
    private const int EventTableCountOffset = 8;
    private const int EventTaskOffset = 6;
    private const string EvntSignature = "EVNT";
    private const int IdentifiedEntrySize = 12;
    private const int IdentifiedMessageIdOffset = 4;
    private const int IdentifiedNameDataOffset = 8;
    private const int KeywordEntrySize = 16;
    private const int KeywordMessageIdOffset = 8;
    private const int KeywordNameDataOffset = 12;
    private const string KeywSignature = "KEYW";
    private const string LevlSignature = "LEVL";
    private const int MapEntryArrayOffset = 20;
    private const int MapEntrySize = 8;
    private const int MapNameOffset = 8;
    private const int MapValueCountOffset = 16;
    private const uint MaxElementCount = 4096;
    private const uint MaxEventCount = 65536;
    private const uint MaxMapEntryCount = 65536;
    private const uint MaxNameByteLength = 4096;
    private const uint MaxProviderCount = 4096;
    private const long MaxResourceSize = 64L * 1024 * 1024;
    private const uint MaxTableEntryCount = 65536;
    private const uint MaxTemplateItemCount = 4096;
    private const int MinResourceSize = 16;
    private const string OpcoSignature = "OPCO";
    private const int TableEntryArrayOffset = 12;
    private const int TableEntryCountOffset = 8;
    private const int TaskEntryNameDataOffset = 24;
    private const int TaskEntrySize = 28;
    private const string TaskSignature = "TASK";
    private const int TemplateItemArrayCountOffset = 12;
    private const int TemplateItemCountOffset = 8;
    private const int TemplateItemFlagsOffset = 0;
    private const int TemplateItemInTypeOffset = 4;
    private const int TemplateItemLengthOffset = 14;
    private const int TemplateItemMapOffset = 8;
    private const int TemplateItemNameOffset = 16;
    private const int TemplateItemOutTypeOffset = 5;
    private const int TemplateItemSize = 20;
    private const int TemplateItemsPointerOffset = 16;
    private const int TemplateNameCountOffset = 12;
    private const string TempSignature = "TEMP";
    private const string VmapSignature = "VMAP";
    private const int WevtElementArrayOffset = 20;
    private const int WevtElementCountOffset = 12;
    private const int WevtElementDescriptorSize = 8;
    private const string WevtResourceName = "#1";
    private const string WevtResourceType = "WEVT_TEMPLATE";
    private const string WevtSignature = "WEVT";

    /// <summary>
    ///     Parses the map API view of a provider: the value-map / bitMap definitions and per-event field-to-map
    ///     associations. Returns <c>null</c> when the provider has no value maps (legacy contract), even though the full parse
    ///     may still carry events and tables.
    /// </summary>
    internal static WevtTemplateData? TryParse(ReadOnlySpan<byte> data, Guid publisherGuid, ITraceLogger? logger)
    {
        WevtProviderData? provider = TryParseProvider(data, publisherGuid, logger);

        return provider is null || provider.Templates.Maps.Count == 0 ? null : provider.Templates;
    }

    /// <summary>Parses the full provider tables in a single pass. Name strings are materialized; no span escapes.</summary>
    internal static WevtProviderData? TryParseProvider(ReadOnlySpan<byte> data, Guid publisherGuid, ITraceLogger? logger)
    {
        if (!TryReadSignature(data, 0, out string signature) || signature != CrimSignature)
        {
            return null;
        }

        if (!TryReadUInt32(data, CrimProviderCountOffset, out uint providerCount))
        {
            return null;
        }

        if (!TryFindProviderOffset(data, providerCount, publisherGuid, out uint providerOffset))
        {
            logger?.Debug($"{nameof(WevtTemplateReader)}: provider {publisherGuid} not found in WEVT_TEMPLATE.");

            return null;
        }

        if (!TryReadSignature(data, (int)providerOffset, out string providerSignature) ||
            providerSignature != WevtSignature)
        {
            return null;
        }

        if (!TryReadUInt32(data, (int)providerOffset + WevtElementCountOffset, out uint elementCount) ||
            elementCount > MaxElementCount)
        {
            return null;
        }

        uint eventTableOffset = 0;
        uint channelTableOffset = 0;
        uint levelTableOffset = 0;
        uint opcodeTableOffset = 0;
        uint taskTableOffset = 0;
        uint keywordTableOffset = 0;

        for (uint elementIndex = 0; elementIndex < elementCount; elementIndex++)
        {
            int descriptorOffset =
                (int)providerOffset + WevtElementArrayOffset + (int)(elementIndex * WevtElementDescriptorSize);

            if (!TryReadUInt32(data, descriptorOffset, out uint elementOffset))
            {
                break;
            }

            if (!TryReadSignature(data, (int)elementOffset, out string elementSignature))
            {
                continue;
            }

            switch (elementSignature)
            {
                case EvntSignature: eventTableOffset = elementOffset; break;
                case ChanSignature: channelTableOffset = elementOffset; break;
                case LevlSignature: levelTableOffset = elementOffset; break;
                case OpcoSignature: opcodeTableOffset = elementOffset; break;
                case TaskSignature: taskTableOffset = elementOffset; break;
                case KeywSignature: keywordTableOffset = elementOffset; break;
            }
        }

        Dictionary<string, WevtRawMap> maps = new(StringComparer.Ordinal);
        Dictionary<WevtEventKey, IReadOnlyDictionary<string, string>> eventFieldMaps = [];

        IReadOnlyList<WevtProviderEvent> events = eventTableOffset == 0
            ? []
            : ParseEvents(data, eventTableOffset, maps, eventFieldMaps, logger);

        return new WevtProviderData
        {
            Templates = new WevtTemplateData { Maps = maps, EventFieldMaps = eventFieldMaps },
            Events = events,
            Channels = channelTableOffset == 0 ? [] : ParseChannels(data, channelTableOffset),
            Levels = levelTableOffset == 0 ? [] : ParseIdentifiedTable(data, levelTableOffset, IdentifiedEntrySize),
            Opcodes = opcodeTableOffset == 0 ? [] : ParseIdentifiedTable(data, opcodeTableOffset, IdentifiedEntrySize),
            Tasks = taskTableOffset == 0 ? [] : ParseTasks(data, taskTableOffset),
            Keywords = keywordTableOffset == 0 ? [] : ParseKeywords(data, keywordTableOffset)
        };
    }

    internal static WevtTemplateData? TryRead(string resourceFilePath, Guid publisherGuid, ITraceLogger? logger)
    {
        byte[]? resourceBytes = TryLoadWevtResource(resourceFilePath, logger);

        if (resourceBytes is null || resourceBytes.Length < MinResourceSize)
        {
            return null;
        }

        try
        {
            return TryParse(resourceBytes, publisherGuid, logger);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException
                                       and not StackOverflowException
                                       and not AccessViolationException)
        {
            logger?.Debug(
                $"{nameof(WevtTemplateReader)}: failed to parse WEVT_TEMPLATE from {resourceFilePath}: {ex.Message}");

            return null;
        }
    }

    /// <summary>
    ///     Rents an <see cref="ArrayPool{T}" /> buffer holding the WEVT_TEMPLATE resource. The caller owns the buffer and
    ///     MUST return it; only the first <paramref name="resourceSize" /> bytes are valid (the rented buffer is oversized).
    /// </summary>
    internal static byte[]? TryRentWevtResource(string resourceFilePath, ITraceLogger? logger, out int resourceSize) =>
        TryCopyWevtResource(resourceFilePath, logger, static size => ArrayPool<byte>.Shared.Rent(size), out resourceSize);

    private static List<WevtChannelEntry> ParseChannels(ReadOnlySpan<byte> data, uint tableOffset)
    {
        List<WevtChannelEntry> channels = [];

        if (!TryReadUInt32(data, (int)tableOffset + TableEntryCountOffset, out uint count) || count > MaxTableEntryCount)
        {
            return channels;
        }

        for (uint index = 0; index < count; index++)
        {
            int entryOffset = (int)tableOffset + TableEntryArrayOffset + (int)(index * ChannelEntrySize);

            if (!TryReadUInt32(data, entryOffset, out uint id) ||
                !TryReadUInt32(data, entryOffset + ChannelNameDataOffset, out uint nameDataOffset) ||
                !TryReadUInt32(data, entryOffset + ChannelAuxOffset, out uint referenceId) ||
                !TryReadUInt32(data, entryOffset + ChannelMessageIdOffset, out uint messageId))
            {
                break;
            }

            channels.Add(new WevtChannelEntry(id, referenceId, messageId, ReadInlineName(data, nameDataOffset)));
        }

        return channels;
    }

    private static List<WevtProviderEvent> ParseEvents(
        ReadOnlySpan<byte> data,
        uint eventTableOffset,
        Dictionary<string, WevtRawMap> maps,
        Dictionary<WevtEventKey, IReadOnlyDictionary<string, string>> eventFieldMaps,
        ITraceLogger? logger)
    {
        List<WevtProviderEvent> events = [];

        if (!TryReadUInt32(data, (int)eventTableOffset + EventTableCountOffset, out uint eventCount) ||
            eventCount > MaxEventCount)
        {
            return events;
        }

        Dictionary<uint, string> mapNamesByOffset = [];
        Dictionary<uint, Dictionary<string, string>?> fieldMapsByTemplateOffset = [];
        Dictionary<uint, WevtParsedTemplate?> templatesByOffset = [];

        for (uint eventIndex = 0; eventIndex < eventCount; eventIndex++)
        {
            int eventDefinitionOffset =
                (int)eventTableOffset + EventTableArrayOffset + (int)(eventIndex * EventDefinitionSize);

            if (!TryReadUInt16(data, eventDefinitionOffset, out ushort eventId) ||
                !TryReadByte(data, eventDefinitionOffset + EventDefinitionVersionOffset, out byte version) ||
                !TryReadByte(data, eventDefinitionOffset + EventChannelOffset, out byte channel) ||
                !TryReadByte(data, eventDefinitionOffset + EventLevelOffset, out byte level) ||
                !TryReadByte(data, eventDefinitionOffset + EventOpcodeOffset, out byte opcode) ||
                !TryReadUInt16(data, eventDefinitionOffset + EventTaskOffset, out ushort task) ||
                !TryReadUInt64(data, eventDefinitionOffset + EventKeywordsOffset, out ulong keywords) ||
                !TryReadUInt32(data, eventDefinitionOffset + EventMessageIdOffset, out uint messageId) ||
                !TryReadUInt32(data, eventDefinitionOffset + EventDefinitionTemplateOffset, out uint templateOffset))
            {
                continue;
            }

            WevtParsedTemplate? template = null;

            if (templateOffset != 0)
            {
                if (!fieldMapsByTemplateOffset.TryGetValue(templateOffset, out Dictionary<string, string>? fieldMaps))
                {
                    fieldMaps = ParseTemplate(data, templateOffset, maps, mapNamesByOffset, logger);
                    fieldMapsByTemplateOffset[templateOffset] = fieldMaps;
                }

                if (fieldMaps is { Count: > 0 })
                {
                    eventFieldMaps[new WevtEventKey(eventId, version)] = fieldMaps;
                }

                if (!templatesByOffset.TryGetValue(templateOffset, out template))
                {
                    template = ParseTemplateItems(data, templateOffset);
                    templatesByOffset[templateOffset] = template;
                }
            }

            events.Add(new WevtProviderEvent(eventId, version, channel, level, opcode, task, keywords, messageId, template));
        }

        return events;
    }

    private static List<WevtIdentifiedEntry> ParseIdentifiedTable(ReadOnlySpan<byte> data, uint tableOffset, int entrySize)
    {
        // LEVL / OPCO share the 12-byte layout: id@0, messageId@4, nameDataOffset@8.
        List<WevtIdentifiedEntry> entries = [];

        if (!TryReadUInt32(data, (int)tableOffset + TableEntryCountOffset, out uint count) || count > MaxTableEntryCount)
        {
            return entries;
        }

        for (uint index = 0; index < count; index++)
        {
            int entryOffset = (int)tableOffset + TableEntryArrayOffset + (int)(index * entrySize);

            if (!TryReadUInt32(data, entryOffset, out uint id) ||
                !TryReadUInt32(data, entryOffset + IdentifiedMessageIdOffset, out uint messageId) ||
                !TryReadUInt32(data, entryOffset + IdentifiedNameDataOffset, out uint nameDataOffset))
            {
                break;
            }

            entries.Add(new WevtIdentifiedEntry(id, messageId, ReadInlineName(data, nameDataOffset)));
        }

        return entries;
    }

    private static List<WevtKeywordEntry> ParseKeywords(ReadOnlySpan<byte> data, uint tableOffset)
    {
        // KEYW entry is 16 bytes: bit mask(u64)@0, messageId@8, nameDataOffset@12.
        List<WevtKeywordEntry> keywords = [];

        if (!TryReadUInt32(data, (int)tableOffset + TableEntryCountOffset, out uint count) || count > MaxTableEntryCount)
        {
            return keywords;
        }

        for (uint index = 0; index < count; index++)
        {
            int entryOffset = (int)tableOffset + TableEntryArrayOffset + (int)(index * KeywordEntrySize);

            if (!TryReadUInt64(data, entryOffset, out ulong mask) ||
                !TryReadUInt32(data, entryOffset + KeywordMessageIdOffset, out uint messageId) ||
                !TryReadUInt32(data, entryOffset + KeywordNameDataOffset, out uint nameDataOffset))
            {
                break;
            }

            keywords.Add(new WevtKeywordEntry(mask, messageId, ReadInlineName(data, nameDataOffset)));
        }

        return keywords;
    }

    private static List<WevtIdentifiedEntry> ParseTasks(ReadOnlySpan<byte> data, uint tableOffset)
    {
        // TASK entry is 28 bytes: id@0, messageId@4, mui-guid@8[16], nameDataOffset@24.
        List<WevtIdentifiedEntry> tasks = [];

        if (!TryReadUInt32(data, (int)tableOffset + TableEntryCountOffset, out uint count) || count > MaxTableEntryCount)
        {
            return tasks;
        }

        for (uint index = 0; index < count; index++)
        {
            int entryOffset = (int)tableOffset + TableEntryArrayOffset + (int)(index * TaskEntrySize);

            if (!TryReadUInt32(data, entryOffset, out uint id) ||
                !TryReadUInt32(data, entryOffset + IdentifiedMessageIdOffset, out uint messageId) ||
                !TryReadUInt32(data, entryOffset + TaskEntryNameDataOffset, out uint nameDataOffset))
            {
                break;
            }

            tasks.Add(new WevtIdentifiedEntry(id, messageId, ReadInlineName(data, nameDataOffset)));
        }

        return tasks;
    }

    private static Dictionary<string, string>? ParseTemplate(
        ReadOnlySpan<byte> data,
        uint templateOffset,
        Dictionary<string, WevtRawMap> maps,
        Dictionary<uint, string> mapNamesByOffset,
        ITraceLogger? logger)
    {
        if (!TryReadSignature(data, (int)templateOffset, out string signature) || signature != TempSignature)
        {
            return null;
        }

        if (!TryReadUInt32(data, (int)templateOffset + TemplateItemCountOffset, out uint itemCount) ||
            !TryReadUInt32(data, (int)templateOffset + TemplateItemsPointerOffset, out uint itemsOffset) ||
            itemCount > MaxTemplateItemCount)
        {
            return null;
        }

        Dictionary<string, string>? fieldMaps = null;

        for (uint itemIndex = 0; itemIndex < itemCount; itemIndex++)
        {
            int itemOffset = (int)itemsOffset + (int)(itemIndex * TemplateItemSize);

            if (!TryReadUInt32(data, itemOffset + TemplateItemMapOffset, out uint mapOffset) ||
                !TryReadUInt32(data, itemOffset + TemplateItemNameOffset, out uint nameOffset))
            {
                continue;
            }

            if (mapOffset == 0)
            {
                continue;
            }

            if (!TryReadName(data, nameOffset, out string fieldName) || fieldName.Length == 0)
            {
                continue;
            }

            string? mapName = ResolveMapName(data, mapOffset, maps, mapNamesByOffset, logger);

            if (mapName is null)
            {
                continue;
            }

            fieldMaps ??= new Dictionary<string, string>(StringComparer.Ordinal);
            fieldMaps[fieldName] = mapName;
        }

        return fieldMaps;
    }

    private static WevtParsedTemplate? ParseTemplateItems(ReadOnlySpan<byte> data, uint templateOffset)
    {
        if (!TryReadSignature(data, (int)templateOffset, out string signature) || signature != TempSignature)
        {
            return null;
        }

        if (!TryReadUInt32(data, (int)templateOffset + TemplateItemCountOffset, out uint itemCount) ||
            !TryReadUInt32(data, (int)templateOffset + TemplateNameCountOffset, out uint nameCount) ||
            !TryReadUInt32(data, (int)templateOffset + TemplateItemsPointerOffset, out uint itemsOffset) ||
            itemCount > MaxTemplateItemCount)
        {
            return null;
        }

        // A struct-bearing template has more name entries than descriptor items (numNames > numDesc). Nested struct
        // synthesis is out of scope for P2a, so the template fails closed and the offline reader emits no template XML.
        bool isStruct = nameCount > itemCount;

        List<WevtTemplateItem> items = new((int)itemCount);

        for (uint itemIndex = 0; itemIndex < itemCount; itemIndex++)
        {
            int itemOffset = (int)itemsOffset + (int)(itemIndex * TemplateItemSize);

            if (!TryReadUInt32(data, itemOffset + TemplateItemFlagsOffset, out uint flags) ||
                !TryReadByte(data, itemOffset + TemplateItemInTypeOffset, out byte inType) ||
                !TryReadByte(data, itemOffset + TemplateItemOutTypeOffset, out byte outType) ||
                !TryReadUInt16(data, itemOffset + TemplateItemArrayCountOffset, out ushort count) ||
                !TryReadUInt16(data, itemOffset + TemplateItemLengthOffset, out ushort lengthRefIndex) ||
                !TryReadUInt32(data, itemOffset + TemplateItemNameOffset, out uint nameOffset) ||
                !TryReadName(data, nameOffset, out string name))
            {
                // A truncated or malformed item descriptor or name makes the whole template unrepresentable, so it fails
                // closed (the offline reader emits no template XML) rather than synthesizing a partial / empty-name template.
                return null;
            }

            items.Add(new WevtTemplateItem(name, inType, outType, count, flags, lengthRefIndex));
        }

        return new WevtParsedTemplate { IsStruct = isStruct, Items = items };
    }

    private static string? ReadInlineName(ReadOnlySpan<byte> data, uint nameDataOffset)
    {
        if (nameDataOffset == 0 || !TryReadName(data, nameDataOffset, out string name) || name.Length == 0)
        {
            return null;
        }

        return name;
    }

    private static string? ResolveMapName(
        ReadOnlySpan<byte> data,
        uint mapOffset,
        Dictionary<string, WevtRawMap> maps,
        Dictionary<uint, string> mapNamesByOffset,
        ITraceLogger? logger)
    {
        if (mapNamesByOffset.TryGetValue(mapOffset, out string? cachedName))
        {
            return cachedName;
        }

        if (!TryParseMap(data, mapOffset, out string mapName, out WevtRawMap? rawMap) || rawMap is null)
        {
            logger?.Debug($"{nameof(WevtTemplateReader)}: failed to parse map at offset {mapOffset}.");

            return null;
        }

        mapNamesByOffset[mapOffset] = mapName;
        maps.TryAdd(mapName, rawMap);

        return mapName;
    }

    private static byte[]? TryCopyWevtResource(
        string resourceFilePath,
        ITraceLogger? logger,
        Func<int, byte[]> allocate,
        out int resourceSize)
    {
        resourceSize = 0;

        if (string.IsNullOrEmpty(resourceFilePath) ||
            !Path.IsPathFullyQualified(resourceFilePath) ||
            !File.Exists(resourceFilePath))
        {
            return null;
        }

        LibraryHandle module = NativeMethods.LoadLibraryExW(
            resourceFilePath,
            IntPtr.Zero,
            LoadLibraryFlags.LOAD_LIBRARY_AS_DATAFILE);

        if (module.IsInvalid)
        {
            module.Dispose();
            logger?.Debug($"{nameof(WevtTemplateReader)}: LoadLibraryExW failed for {resourceFilePath}.");

            return null;
        }

        try
        {
            IntPtr resourceInfo = NativeMethods.FindResourceW(module, WevtResourceName, WevtResourceType);

            if (resourceInfo == IntPtr.Zero)
            {
                return null;
            }

            IntPtr resourceData = NativeMethods.LoadResource(module, resourceInfo);

            if (resourceData == IntPtr.Zero)
            {
                return null;
            }

            IntPtr resourcePointer = NativeMethods.LockResource(resourceData);

            if (resourcePointer == IntPtr.Zero)
            {
                return null;
            }

            uint size = NativeMethods.SizeofResource(module, resourceInfo);

            if (size is 0 || size > MaxResourceSize)
            {
                return null;
            }

            byte[] buffer = allocate((int)size);
            Marshal.Copy(resourcePointer, buffer, 0, (int)size);
            resourceSize = (int)size;

            return buffer;
        }
        finally
        {
            module.Dispose();
        }
    }

    private static bool TryFindProviderOffset(
        ReadOnlySpan<byte> data,
        uint providerCount,
        Guid publisherGuid,
        out uint providerOffset)
    {
        providerOffset = 0;

        if (providerCount > MaxProviderCount)
        {
            return false;
        }

        for (uint providerIndex = 0; providerIndex < providerCount; providerIndex++)
        {
            int descriptorOffset =
                CrimProviderDescriptorArrayOffset + (int)(providerIndex * CrimProviderDescriptorSize);

            if (!TryReadGuid(data, descriptorOffset, out Guid guid) ||
                !TryReadUInt32(data, descriptorOffset + 16, out uint dataOffset))
            {
                return false;
            }

            if (guid == publisherGuid)
            {
                providerOffset = dataOffset;

                return true;
            }
        }

        return false;
    }

    private static byte[]? TryLoadWevtResource(string resourceFilePath, ITraceLogger? logger) =>
        TryCopyWevtResource(resourceFilePath, logger, static size => new byte[size], out _);

    private static bool TryParseMap(ReadOnlySpan<byte> data, uint mapOffset, out string mapName, out WevtRawMap? rawMap)
    {
        mapName = string.Empty;
        rawMap = null;

        if (!TryReadSignature(data, (int)mapOffset, out string signature) ||
            (signature != VmapSignature && signature != BmapSignature))
        {
            return false;
        }

        if (!TryReadUInt32(data, (int)mapOffset + MapNameOffset, out uint nameOffset) ||
            !TryReadUInt32(data, (int)mapOffset + MapValueCountOffset, out uint valueCount) ||
            valueCount > MaxMapEntryCount)
        {
            return false;
        }

        if (!TryReadName(data, nameOffset, out mapName) || mapName.Length == 0)
        {
            return false;
        }

        List<WevtRawMapEntry> entries = new((int)valueCount);

        for (uint entryIndex = 0; entryIndex < valueCount; entryIndex++)
        {
            int entryOffset = (int)mapOffset + MapEntryArrayOffset + (int)(entryIndex * MapEntrySize);

            if (!TryReadUInt32(data, entryOffset, out uint value) ||
                !TryReadUInt32(data, entryOffset + 4, out uint messageId))
            {
                return false;
            }

            entries.Add(new WevtRawMapEntry(value, messageId));
        }

        rawMap = new WevtRawMap(signature == BmapSignature, entries);

        return true;
    }

    private static bool TryReadByte(ReadOnlySpan<byte> data, int offset, out byte value)
    {
        if (offset < 0 || offset >= data.Length)
        {
            value = 0;

            return false;
        }

        value = data[offset];

        return true;
    }

    private static bool TryReadGuid(ReadOnlySpan<byte> data, int offset, out Guid value)
    {
        if (offset < 0 || offset + 16 > data.Length)
        {
            value = Guid.Empty;

            return false;
        }

        value = new Guid(data.Slice(offset, 16));

        return true;
    }

    private static bool TryReadName(ReadOnlySpan<byte> data, uint nameOffset, out string name)
    {
        name = string.Empty;

        if (!TryReadUInt32(data, (int)nameOffset, out uint totalByteSize) ||
            totalByteSize < 4 ||
            totalByteSize > MaxNameByteLength)
        {
            return false;
        }

        int stringByteLength = (int)totalByteSize - 4;
        int stringStart = (int)nameOffset + 4;

        if (stringStart < 0 || stringStart + stringByteLength > data.Length)
        {
            return false;
        }

        name = Encoding.Unicode.GetString(data.Slice(stringStart, stringByteLength)).TrimEnd('\0');

        return true;
    }

    private static bool TryReadSignature(ReadOnlySpan<byte> data, int offset, out string signature)
    {
        if (offset < 0 || offset + 4 > data.Length)
        {
            signature = string.Empty;

            return false;
        }

        signature = Encoding.ASCII.GetString(data.Slice(offset, 4));

        return true;
    }

    private static bool TryReadUInt16(ReadOnlySpan<byte> data, int offset, out ushort value)
    {
        if (offset < 0 || offset + 2 > data.Length)
        {
            value = 0;

            return false;
        }

        value = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2));

        return true;
    }

    private static bool TryReadUInt32(ReadOnlySpan<byte> data, int offset, out uint value)
    {
        if (offset < 0 || offset + 4 > data.Length)
        {
            value = 0;

            return false;
        }

        value = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));

        return true;
    }

    private static bool TryReadUInt64(ReadOnlySpan<byte> data, int offset, out ulong value)
    {
        if (offset < 0 || offset + 8 > data.Length)
        {
            value = 0;

            return false;
        }

        value = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8));

        return true;
    }
}
