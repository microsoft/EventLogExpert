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

internal abstract record WevtTemplateNode(string Name, uint Flags, ushort ArrayCount);

internal sealed record WevtLeafNode(string Name, byte InType, byte OutType, uint Flags, ushort ArrayCount, ushort Length)
    : WevtTemplateNode(Name, Flags, ArrayCount);

internal sealed record WevtStructNode(string Name, uint Flags, ushort ArrayCount, IReadOnlyList<WevtLeafNode> Members)
    : WevtTemplateNode(Name, Flags, ArrayCount);

internal readonly record struct WevtRawDescriptor(string Name, bool IsStruct);

internal sealed class WevtParsedTemplate
{
    public required IReadOnlyList<WevtRawDescriptor> Descriptors { get; init; }

    public required IReadOnlyList<WevtTemplateNode> Nodes { get; init; }
}

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

// CHAN @8 auxiliary field is the reference id used by each event's channel byte.
internal readonly record struct WevtChannelEntry(uint Id, uint ReferenceId, uint MessageId, string? InlineName);

internal readonly record struct WevtIdentifiedEntry(uint Id, uint MessageId, string? InlineName);

internal readonly record struct WevtKeywordEntry(ulong Mask, uint MessageId, string? InlineName);

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

// Parse WEVT_TEMPLATE directly because EvtOpenPublisherMetadata omits valueMap/bitMap data and map attributes.
internal static class WevtTemplateReader
{
    private const string BmapSignature = "BMAP";
    private const int ChannelAuxOffset = 8;
    private const int ChannelEntrySize = 16;
    private const int ChannelMessageIdOffset = 12;
    private const int ChannelNameDataOffset = 4;
    private const string ChanSignature = "CHAN";
    private const uint ClassicEventIdCustomerBit = 0x20000000;
    private const int CrimProviderCountOffset = 12;
    private const int CrimProviderDescriptorArrayOffset = 16;
    private const int CrimProviderDescriptorSize = 20;
    private const string CrimSignature = "CRIM";
    private const int EventChannelOffset = 3;
    private const int EventClassicTrailingFieldOffset = 44;
    private const int EventDefinitionSize = 48;
    private const int EventDefinitionTemplateOffset = 20;
    private const int EventDefinitionVersionOffset = 2;
    private const int EventKeywordsOffset = 8;
    private const int EventLevelOffset = 4;
    private const int EventMessageIdOffset = 16;
    private const int EventOpcodeOffset = 5;
    private const int EventOpcodeTableReferenceOffset = 24;
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
    private const int TemplateItemMemberCountOffset = 6;
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

    internal static WevtTemplateData? TryParse(ReadOnlySpan<byte> data, Guid publisherGuid, ITraceLogger? logger)
    {
        WevtProviderData? provider = TryParseProvider(data, publisherGuid, logger);

        return provider is null || provider.Templates.Maps.Count == 0 ? null : provider.Templates;
    }

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

    // Caller owns the rented buffer; only the first resourceSize bytes are valid.
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

            bool isClassicEvent =
                TryReadUInt32(data,
                    eventDefinitionOffset + EventOpcodeTableReferenceOffset,
                    out uint opcodeTableReference) &&
                opcodeTableReference == 0 &&
                TryReadUInt32(data,
                    eventDefinitionOffset + EventClassicTrailingFieldOffset,
                    out uint classicTrailingField) &&
                classicTrailingField == 0 &&
                (messageId & ClassicEventIdCustomerBit) == 0;

            uint nativeId = eventId;
            byte nativeVersion = version;
            byte nativeOpcode = opcode;

            if (isClassicEvent)
            {
                nativeId = ((uint)opcode << 24) | ((uint)version << 16) | eventId;
                nativeVersion = 0;
                nativeOpcode = 0;
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
                    eventFieldMaps[new WevtEventKey(nativeId, nativeVersion)] = fieldMaps;
                }

                if (!templatesByOffset.TryGetValue(templateOffset, out template))
                {
                    template = ParseTemplateItems(data, templateOffset);
                    templatesByOffset[templateOffset] = template;
                }
            }

            events.Add(new WevtProviderEvent(nativeId, nativeVersion, channel, level, nativeOpcode, task, keywords, messageId, template));
        }

        return events;
    }

    private static List<WevtIdentifiedEntry> ParseIdentifiedTable(ReadOnlySpan<byte> data, uint tableOffset, int entrySize)
    {
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
            itemCount > MaxTemplateItemCount ||
            nameCount > MaxTemplateItemCount ||
            nameCount < itemCount)
        {
            return null;
        }

        RawTemplateDescriptor[] raw = new RawTemplateDescriptor[nameCount];
        WevtRawDescriptor[] descriptors = new WevtRawDescriptor[nameCount];

        for (uint index = 0; index < nameCount; index++)
        {
            int itemOffset = (int)itemsOffset + (int)(index * TemplateItemSize);

            if (!TryReadUInt32(data, itemOffset + TemplateItemFlagsOffset, out uint flags) ||
                !TryReadByte(data, itemOffset + TemplateItemInTypeOffset, out byte inType) ||
                !TryReadByte(data, itemOffset + TemplateItemOutTypeOffset, out byte outType) ||
                !TryReadUInt16(data, itemOffset + TemplateItemMemberCountOffset, out ushort memberCount) ||
                !TryReadUInt16(data, itemOffset + TemplateItemInTypeOffset, out ushort memberStart) ||
                !TryReadUInt16(data, itemOffset + TemplateItemArrayCountOffset, out ushort arrayCount) ||
                !TryReadUInt16(data, itemOffset + TemplateItemLengthOffset, out ushort length) ||
                !TryReadUInt32(data, itemOffset + TemplateItemNameOffset, out uint nameOffset) ||
                !TryReadName(data, nameOffset, out string name))
            {
                return null;
            }

            raw[index] = new RawTemplateDescriptor(name, inType, outType, memberCount, memberStart, flags, arrayCount, length);
            descriptors[index] = new WevtRawDescriptor(name, memberCount > 0);
        }

        List<WevtTemplateNode> nodes = new((int)itemCount);
        bool[] memberConsumed = new bool[nameCount];

        for (uint index = 0; index < itemCount; index++)
        {
            RawTemplateDescriptor descriptor = raw[index];

            if (descriptor.MemberCount == 0)
            {
                nodes.Add(ToLeafNode(descriptor));

                continue;
            }

            long memberEnd = (long)descriptor.MemberStart + descriptor.MemberCount;

            if (descriptor.MemberStart < itemCount || memberEnd > nameCount)
            {
                return null;
            }

            List<WevtLeafNode> members = new(descriptor.MemberCount);

            for (int member = descriptor.MemberStart; member < memberEnd; member++)
            {
                // Nested or multiply-claimed struct members are unrepresentable in manifest XML.
                if (raw[member].MemberCount != 0 || memberConsumed[member])
                {
                    return null;
                }

                memberConsumed[member] = true;
                members.Add(ToLeafNode(raw[member]));
            }

            nodes.Add(new WevtStructNode(descriptor.Name, descriptor.Flags, descriptor.ArrayCount, members));
        }

        for (int index = (int)itemCount; index < nameCount; index++)
        {
            // Appended member descriptors must be claimed by exactly one struct.
            if (!memberConsumed[index])
            {
                return null;
            }
        }

        return new WevtParsedTemplate { Nodes = nodes, Descriptors = descriptors };
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

    private static WevtLeafNode ToLeafNode(RawTemplateDescriptor descriptor) =>
        new(descriptor.Name, descriptor.InType, descriptor.OutType, descriptor.Flags, descriptor.ArrayCount, descriptor.Length);

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
        if (offset < 0 || offset > data.Length - 16)
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

        if (stringStart < 0 || stringStart > data.Length - stringByteLength)
        {
            return false;
        }

        name = Encoding.Unicode.GetString(data.Slice(stringStart, stringByteLength)).TrimEnd('\0');

        return true;
    }

    private static bool TryReadSignature(ReadOnlySpan<byte> data, int offset, out string signature)
    {
        if (offset < 0 || offset > data.Length - 4)
        {
            signature = string.Empty;

            return false;
        }

        signature = Encoding.ASCII.GetString(data.Slice(offset, 4));

        return true;
    }

    private static bool TryReadUInt16(ReadOnlySpan<byte> data, int offset, out ushort value)
    {
        if (offset < 0 || offset > data.Length - 2)
        {
            value = 0;

            return false;
        }

        value = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2));

        return true;
    }

    private static bool TryReadUInt32(ReadOnlySpan<byte> data, int offset, out uint value)
    {
        if (offset < 0 || offset > data.Length - 4)
        {
            value = 0;

            return false;
        }

        value = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));

        return true;
    }

    private static bool TryReadUInt64(ReadOnlySpan<byte> data, int offset, out ulong value)
    {
        if (offset < 0 || offset > data.Length - 8)
        {
            value = 0;

            return false;
        }

        value = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8));

        return true;
    }

    private readonly record struct RawTemplateDescriptor(
        string Name,
        byte InType,
        byte OutType,
        ushort MemberCount,
        ushort MemberStart,
        uint Flags,
        ushort ArrayCount,
        ushort Length);
}
