// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Interop;
using EventLogExpert.Logging.Abstractions;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace EventLogExpert.Eventing.PublisherMetadata;

internal readonly record struct WevtRawMapEntry(uint Value, uint MessageId);

internal sealed record WevtRawMap(bool IsBitMap, IReadOnlyList<WevtRawMapEntry> Entries);

internal readonly record struct WevtEventKey(uint Id, byte Version);

internal sealed class WevtTemplateData
{
    public required IReadOnlyDictionary<WevtEventKey, IReadOnlyDictionary<string, string>> EventFieldMaps { get; init; }

    public required IReadOnlyDictionary<string, WevtRawMap> Maps { get; init; }
}

/// <summary>
///     Reads the binary WEVT_TEMPLATE resource embedded in a provider DLL and extracts its valueMap / bitMap
///     definitions and per-event field-to-map associations.
/// </summary>
/// <remarks>
///     EvtOpenPublisherMetadata exposes no valueMap / bitMap tables and strips the <c>map</c> attribute from the
///     template XML, so the decoded names (for example a bus type of <c>10</c> shown as <c>SAS</c>) are recovered by
///     parsing the compiled resource directly. Every offset is bounds-checked; a malformed resource yields <c>null</c>.
/// </remarks>
internal static class WevtTemplateReader
{
    private const string BmapSignature = "BMAP";
    private const int CrimProviderCountOffset = 12;
    private const int CrimProviderDescriptorArrayOffset = 16;
    private const int CrimProviderDescriptorSize = 20;
    private const string CrimSignature = "CRIM";
    private const int EventDefinitionSize = 48;
    private const int EventDefinitionTemplateOffset = 20;
    private const int EventDefinitionVersionOffset = 2;
    private const int EventTableArrayOffset = 16;
    private const int EventTableCountOffset = 8;
    private const string EvntSignature = "EVNT";
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
    private const uint MaxTemplateItemCount = 4096;
    private const int MinResourceSize = 16;
    private const int TemplateItemCountOffset = 8;
    private const int TemplateItemMapOffset = 8;
    private const int TemplateItemNameOffset = 16;
    private const int TemplateItemSize = 20;
    private const int TemplateItemsPointerOffset = 16;
    private const string TempSignature = "TEMP";
    private const string VmapSignature = "VMAP";
    private const int WevtElementArrayOffset = 20;
    private const int WevtElementCountOffset = 12;
    private const int WevtElementDescriptorSize = 8;
    private const string WevtResourceName = "#1";
    private const string WevtResourceType = "WEVT_TEMPLATE";
    private const string WevtSignature = "WEVT";

    internal static WevtTemplateData? TryParse(byte[] data, Guid publisherGuid, ITraceLogger? logger)
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

        return ParseProvider(data, providerOffset, logger);
    }

    internal static WevtTemplateData? TryRead(string resourceFilePath, Guid publisherGuid, ITraceLogger? logger)
    {
        if (string.IsNullOrEmpty(resourceFilePath) ||
            !Path.IsPathFullyQualified(resourceFilePath) ||
            !File.Exists(resourceFilePath))
        {
            return null;
        }

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

    private static WevtTemplateData? ParseProvider(byte[] data, uint providerOffset, ITraceLogger? logger)
    {
        if (!TryFindEventTableOffset(data, providerOffset, out uint eventTableOffset))
        {
            return null;
        }

        if (!TryReadUInt32(data, (int)eventTableOffset + EventTableCountOffset, out uint eventCount) ||
            eventCount > MaxEventCount)
        {
            return null;
        }

        Dictionary<string, WevtRawMap> maps = new(StringComparer.Ordinal);
        Dictionary<uint, string> mapNamesByOffset = [];
        Dictionary<uint, Dictionary<string, string>?> fieldMapsByTemplateOffset = [];
        Dictionary<WevtEventKey, IReadOnlyDictionary<string, string>> eventFieldMaps = [];

        for (uint eventIndex = 0; eventIndex < eventCount; eventIndex++)
        {
            int eventDefinitionOffset =
                (int)eventTableOffset + EventTableArrayOffset + (int)(eventIndex * EventDefinitionSize);

            if (!TryReadUInt16(data, eventDefinitionOffset, out ushort eventId) ||
                !TryReadByte(data, eventDefinitionOffset + EventDefinitionVersionOffset, out byte version) ||
                !TryReadUInt32(data, eventDefinitionOffset + EventDefinitionTemplateOffset, out uint templateOffset))
            {
                continue;
            }

            if (templateOffset == 0)
            {
                continue;
            }

            if (!fieldMapsByTemplateOffset.TryGetValue(templateOffset, out Dictionary<string, string>? fieldMaps))
            {
                fieldMaps = ParseTemplate(data, templateOffset, maps, mapNamesByOffset, logger);
                fieldMapsByTemplateOffset[templateOffset] = fieldMaps;
            }

            if (fieldMaps is { Count: > 0 })
            {
                eventFieldMaps[new WevtEventKey(eventId, version)] = fieldMaps;
            }
        }

        return maps.Count == 0 ? null : new WevtTemplateData { Maps = maps, EventFieldMaps = eventFieldMaps };
    }

    private static Dictionary<string, string>? ParseTemplate(
        byte[] data,
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

    private static string? ResolveMapName(
        byte[] data,
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

    private static bool TryFindEventTableOffset(byte[] data, uint providerOffset, out uint eventTableOffset)
    {
        eventTableOffset = 0;

        if (!TryReadSignature(data, (int)providerOffset, out string signature) || signature != WevtSignature)
        {
            return false;
        }

        if (!TryReadUInt32(data, (int)providerOffset + WevtElementCountOffset, out uint elementCount) ||
            elementCount > MaxElementCount)
        {
            return false;
        }

        for (uint elementIndex = 0; elementIndex < elementCount; elementIndex++)
        {
            int descriptorOffset =
                (int)providerOffset + WevtElementArrayOffset + (int)(elementIndex * WevtElementDescriptorSize);

            if (!TryReadUInt32(data, descriptorOffset, out uint elementOffset))
            {
                return false;
            }

            if (TryReadSignature(data, (int)elementOffset, out string elementSignature) &&
                elementSignature == EvntSignature)
            {
                eventTableOffset = elementOffset;

                return true;
            }
        }

        return false;
    }

    private static bool TryFindProviderOffset(byte[] data, uint providerCount, Guid publisherGuid, out uint providerOffset)
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

    private static byte[]? TryLoadWevtResource(string resourceFilePath, ITraceLogger? logger)
    {
        LibraryHandle module = NativeMethods.LoadLibraryExW(
            resourceFilePath,
            IntPtr.Zero,
            LoadLibraryFlags.LOAD_LIBRARY_AS_DATAFILE);

        if (module.IsInvalid)
        {
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

            uint resourceSize = NativeMethods.SizeofResource(module, resourceInfo);

            if (resourceSize is 0 || resourceSize > MaxResourceSize)
            {
                return null;
            }

            byte[] buffer = new byte[resourceSize];
            Marshal.Copy(resourcePointer, buffer, 0, (int)resourceSize);

            return buffer;
        }
        finally
        {
            module.Dispose();
        }
    }

    private static bool TryParseMap(byte[] data, uint mapOffset, out string mapName, out WevtRawMap? rawMap)
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

    private static bool TryReadByte(byte[] data, int offset, out byte value)
    {
        if (offset < 0 || offset >= data.Length)
        {
            value = 0;

            return false;
        }

        value = data[offset];

        return true;
    }

    private static bool TryReadGuid(byte[] data, int offset, out Guid value)
    {
        if (offset < 0 || offset + 16 > data.Length)
        {
            value = Guid.Empty;

            return false;
        }

        value = new Guid(data.AsSpan(offset, 16));

        return true;
    }

    private static bool TryReadName(byte[] data, uint nameOffset, out string name)
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

        name = Encoding.Unicode.GetString(data, stringStart, stringByteLength).TrimEnd('\0');

        return true;
    }

    private static bool TryReadSignature(byte[] data, int offset, out string signature)
    {
        if (offset < 0 || offset + 4 > data.Length)
        {
            signature = string.Empty;

            return false;
        }

        signature = Encoding.ASCII.GetString(data, offset, 4);

        return true;
    }

    private static bool TryReadUInt16(byte[] data, int offset, out ushort value)
    {
        if (offset < 0 || offset + 2 > data.Length)
        {
            value = 0;

            return false;
        }

        value = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, 2));

        return true;
    }

    private static bool TryReadUInt32(byte[] data, int offset, out uint value)
    {
        if (offset < 0 || offset + 4 > data.Length)
        {
            value = 0;

            return false;
        }

        value = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));

        return true;
    }
}
