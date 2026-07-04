// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using System.Buffers;
using System.Buffers.Binary;
using System.IO.MemoryMappedFiles;
using System.Text;

namespace EventLogExpert.Eventing.OfflineImaging.Registry;

internal sealed class OfflineHiveFile : IOfflineRegistryKey
{
    private const int BaseBlockSize = 0x1000;       // hive bins start here; cell offsets are relative to this.
    private const uint InvalidOffset = 0xFFFFFFFF;  // hive sentinel for "no cell".
    private const long MaxEntriesPerEnumeration = 100_000; // caps one subkey enumeration's list work.
    private const long MaxHiveTraversalEntries = 16_000_000; // caps whole-hive traversal work.
    private const int MaxListDepth = 16;     // caps cyclic/deep ri -> li/lf/lh nesting.
    private const long MaxNameBytes = 0x400; // caps crafted name-length allocations.
    private const long MaxValueBytes = 16 * 1024 * 1024; // caps crafted value-length allocations.
    private const long MaxValuesPerNode = 100_000;  // caps one value lookup's vk-offset scan.
    private const ushort SigDb = 0x6264;            // "db" (big data)
    private const uint SigHbin = 0x6e696268;        // "hbin" (hive bin header)
    private const ushort SigLf = 0x666c;            // "lf"
    private const ushort SigLh = 0x686c;            // "lh"
    private const ushort SigLi = 0x696c;            // "li"
    private const ushort SigNk = 0x6b6e;            // "nk"
    private const ushort SigRi = 0x6972;            // "ri"
    private const ushort SigVk = 0x6b76;            // "vk"
    private readonly long _binsEnd;                 // cells must fall below the validated contiguous hbin extent.

    private readonly IHiveBytes _bytes;
    private readonly long _length;
    private readonly ITraceLogger? _logger;
    private readonly uint _rootCellOffset;

    private bool _disposed;
    private long _hiveTraversalBudget; // remaining whole-hive list-entry budget.

    private OfflineHiveFile(IHiveBytes bytes, uint rootCellOffset, long binsEnd, bool isDirty, ITraceLogger? logger)
    {
        _bytes = bytes;
        _length = bytes.Length;
        _binsEnd = binsEnd;
        _rootCellOffset = rootCellOffset;
        IsDirty = isDirty;
        _logger = logger;
        _hiveTraversalBudget = MaxHiveTraversalEntries;
    }

    private interface IHiveBytes : IDisposable
    {
        long Length { get; }

        void ReadBytes(long offset, Span<byte> destination);

        int ReadInt32(long offset);

        ushort ReadUInt16(long offset);

        uint ReadUInt32(long offset);
    }

    // Dirty hive: sequence/checksum mismatch; read last-flushed state.
    public bool IsDirty { get; }

    public static OfflineHiveFile? TryOpen(string hiveFilePath, ITraceLogger? logger)
    {
        long fileLength;

        try
        {
            var info = new FileInfo(hiveFilePath);

            if (!info.Exists)
            {
                logger?.Debug($"{nameof(OfflineHiveFile)}: hive file not found: {hiveFilePath}.");

                return null;
            }

            fileLength = info.Length;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            logger?.Debug($"{nameof(OfflineHiveFile)}: cannot stat hive {hiveFilePath}: {ex.Message}");

            return null;
        }

        if (fileLength < BaseBlockSize + 0x20)
        {
            logger?.Error($"The image's hive at {hiveFilePath} is too small to be a registry hive.");

            return null;
        }

        MemoryMappedFile? map = null;
        MemoryMappedViewAccessor? view = null;

        try
        {
            using FileStream stream = File.Open(hiveFilePath, new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.Read | FileShare.Delete
            });

            map = MemoryMappedFile.CreateFromFile(stream, mapName: null, capacity: 0,
                MemoryMappedFileAccess.Read, HandleInheritability.None, leaveOpen: false);
            view = map.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);

            OfflineHiveFile? hive = TryCreate(new MappedHiveBytes(map, view, fileLength), logger);

            if (hive is null)
            {
                view.Dispose();
                map.Dispose();
            }

            return hive;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            view?.Dispose();
            map?.Dispose();
            logger?.Error($"The image's hive at {hiveFilePath} could not be read: {ex.Message}. Reading a protected image hive may require running as administrator, or the file is in use.");

            return null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            view?.Dispose();
            map?.Dispose();
            logger?.Debug($"{nameof(OfflineHiveFile)}: failed to open hive {hiveFilePath}: {ex.Message}");

            return null;
        }
    }

    public static OfflineHiveFile? TryOpen(byte[] hiveBytes, ITraceLogger? logger)
    {
        ArgumentNullException.ThrowIfNull(hiveBytes);

        if (hiveBytes.Length < BaseBlockSize + 0x20)
        {
            logger?.Debug($"{nameof(OfflineHiveFile)}: in-memory hive is too small ({hiveBytes.Length} bytes).");

            return null;
        }

        return TryCreate(new ArrayHiveBytes(hiveBytes), logger);
    }

    public void Dispose()
    {
        if (_disposed) { return; }

        _disposed = true;
        _bytes.Dispose();
    }

    public IReadOnlyList<string> GetSubKeyNames() => GetSubKeyNamesFrom(_rootCellOffset);

    public object? GetValue(string? name) => GetValueFrom(_rootCellOffset, name);

    public IOfflineRegistryKey? OpenSubKey(string path) => OpenSubKeyFrom(_rootCellOffset, path);

    internal IReadOnlyList<string> GetSubKeyNamesFrom(uint fromCellOffset)
    {
        var names = new List<string>();

        try
        {
            foreach (uint childOffset in EnumerateSubkeyOffsets(fromCellOffset))
            {
                if (TryReadKeyName(childOffset, out string name)) { names.Add(name); }
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            _logger?.Debug($"{nameof(OfflineHiveFile)}: enumerating subkeys failed: {ex.Message}");
        }

        return names;
    }

    internal object? GetValueFrom(uint fromCellOffset, string? name)
    {
        try
        {
            return ReadValue(fromCellOffset, name ?? string.Empty);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            _logger?.Debug($"{nameof(OfflineHiveFile)}: reading value '{name}' failed: {ex.Message}");

            return null;
        }
    }

    internal IOfflineRegistryKey? OpenSubKeyFrom(uint fromCellOffset, string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        uint current = fromCellOffset;

        foreach (string segment in path.Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            uint? next = FindChild(current, segment);

            if (next is not { } childOffset) { return null; }

            current = childOffset;
        }

        return new OfflineHiveKey(this, current);
    }

    // Hive bins start at 0x1000; cells are valid only below the contiguous hbin extent.
    private static long ComputeBinsExtent(IHiveBytes bytes)
    {
        long offset = BaseBlockSize;
        long length = bytes.Length;

        while (offset + 0x20 <= length)
        {
            if (bytes.ReadUInt32(offset) != SigHbin) { break; }

            uint binSize = bytes.ReadUInt32(offset + 0x08);

            if (binSize < BaseBlockSize || (binSize & 0xFFF) != 0) { break; } // hbin sizes are 4 KB-aligned.

            long next = offset + binSize;

            if (next <= offset || next > length) { break; }

            offset = next;
        }

        return offset;
    }

    private static object? Decode(uint type, byte[] data) => type switch
    {
        1 or 2 => DecodeString(data), // REG_SZ / REG_EXPAND_SZ (literal)
        4 => data.Length >= 4 ? BinaryPrimitives.ReadInt32LittleEndian(data) : 0, // REG_DWORD -> boxed Int32 parity.
        5 => data.Length >= 4 ? BinaryPrimitives.ReadInt32BigEndian(data) : 0, // REG_DWORD_BIG_ENDIAN -> Int32
        11 => data.Length >= 8 ? BinaryPrimitives.ReadInt64LittleEndian(data) : 0L, // REG_QWORD -> Int64
        7 => DecodeMultiString(data), // REG_MULTI_SZ -> string[]
        _ => data // REG_NONE / REG_BINARY / other
    };

    // REG_MULTI_SZ parity: preserve interior empties and drop only one final terminator.
    private static string[] DecodeMultiString(byte[] data)
    {
        int length = data.Length & ~1;

        if (length == 0) { return []; }

        string decoded = Encoding.Unicode.GetString(data, 0, length);
        int end = decoded.Length;

        if (decoded[end - 1] == '\0') { end--; }

        string[] segments = decoded[..end].Split('\0');

        return segments is [.., ""] ? segments[..^1] : segments;
    }

    private static string DecodeString(byte[] data)
    {
        int length = data.Length & ~1;

        if (length >= 2 && data[length - 1] == 0 && data[length - 2] == 0) { length -= 2; }

        return Encoding.Unicode.GetString(data, 0, length);
    }

    // Base checksum XORs the first 508 bytes; Windows maps 0/0xFFFFFFFF before storing.
    private static bool IsBaseChecksumValid(IHiveBytes bytes)
    {
        uint computed = 0;

        for (long offset = 0; offset < 0x1FC; offset += 4) { computed ^= bytes.ReadUInt32(offset); }

        if (computed == 0) { computed = 1; }
        else if (computed == 0xFFFFFFFF) { computed = 0xFFFFFFFE; }

        return bytes.ReadUInt32(0x1FC) == computed;
    }

    private static OfflineHiveFile? TryCreate(IHiveBytes bytes, ITraceLogger? logger)
    {
        try
        {
            if (bytes.Length < BaseBlockSize)
            {
                logger?.Debug($"{nameof(OfflineHiveFile)}: file too small ({bytes.Length} bytes) to contain a base block.");
                bytes.Dispose();

                return null;
            }

            // Base block: "regf"; sequence numbers classify clean vs dirty; root cell is at 0x24.
            if (bytes.ReadUInt32(0) != 0x66676572u) // "regf"
            {
                logger?.Debug($"{nameof(OfflineHiveFile)}: not a regf hive (bad signature).");
                bytes.Dispose();

                return null;
            }

            uint primarySeq = bytes.ReadUInt32(0x04);
            uint secondarySeq = bytes.ReadUInt32(0x08);
            uint rootCellOffset = bytes.ReadUInt32(0x24);

            // Dirty hive contract: checksum/sequence mismatch warns, then reads last-flushed state.
            bool isDirty = primarySeq != secondarySeq || !IsBaseChecksumValid(bytes);

            if (BaseBlockSize + (long)rootCellOffset + 8 > bytes.Length)
            {
                logger?.Debug($"{nameof(OfflineHiveFile)}: root cell offset 0x{rootCellOffset:X} is out of range.");
                bytes.Dispose();

                return null;
            }

            long binsEnd = ComputeBinsExtent(bytes);
            var hive = new OfflineHiveFile(bytes, rootCellOffset, binsEnd, isDirty, logger);

            if (isDirty)
            {
                logger?.Warning($"The image's registry hive was not cleanly flushed; registry entries changed just before the image was captured may be missing.");
            }

            return hive;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            logger?.Debug($"{nameof(OfflineHiveFile)}: failed to parse base block: {ex.Message}");
            bytes.Dispose();

            return null;
        }
    }

    // Cell offsets are relative to 0x1000; only allocated cells inside validated hbins are readable.
    private long CellData(uint cellOffset)
    {
        if (cellOffset == InvalidOffset) { return -1; }

        long cellStart = BaseBlockSize + (long)cellOffset;

        if (cellStart < BaseBlockSize || cellStart + 4 > _binsEnd) { return -1; }

        int cellSize = _bytes.ReadInt32(cellStart);

        if (cellSize >= 0) { return -1; }

        long cellLength = -(long)cellSize;

        if (cellLength < 8 || cellStart + cellLength > _binsEnd) { return -1; }

        return cellStart + 4;
    }

    private string DecodeName(long offset, int byteCount, bool ascii)
    {
        int clamped = (int)Math.Min(byteCount, MaxNameBytes);
        byte[] buffer = new byte[clamped];
        _bytes.ReadBytes(offset, buffer);

        return ascii ? Encoding.Latin1.GetString(buffer) : Encoding.Unicode.GetString(buffer);
    }

    private IEnumerable<uint> EnumerateSubkeyOffsets(uint nkCellOffset)
    {
        long nk = CellData(nkCellOffset);

        if (nk < 0 || !InBounds(nk, 0x20) || _bytes.ReadUInt16(nk) != SigNk) { yield break; }

        int subkeyCount = _bytes.ReadInt32(nk + 0x14);
        uint subkeyListOffset = _bytes.ReadUInt32(nk + 0x1C);

        if (subkeyCount <= 0 || subkeyListOffset == InvalidOffset) { yield break; }

        // Cap declared subkey counts before list walking.
        if (subkeyCount > Math.Min(MaxEntriesPerEnumeration, _length / 8))
        {
            _logger?.Debug($"{nameof(OfflineHiveFile)}: subkey count {subkeyCount} exceeds the per-enumeration cap; treating as malformed.");

            yield break;
        }

        var visitedLists = new HashSet<uint>();
        var yieldedChildren = new HashSet<uint>();
        var budget = new TraversalBudget(this);

        // Charge every list entry so ri fan-out cannot amplify work unboundedly.
        foreach (uint childOffset in WalkSubkeyList(subkeyListOffset, depth: 0, visitedLists, budget))
        {
            if (yieldedChildren.Add(childOffset)) { yield return childOffset; }
        }
    }

    private uint? FindChild(uint parentCellOffset, string segment)
    {
        foreach (uint childOffset in EnumerateSubkeyOffsets(parentCellOffset))
        {
            if (TryReadKeyName(childOffset, out string name) &&
                string.Equals(name, segment, StringComparison.OrdinalIgnoreCase))
            {
                return childOffset;
            }
        }

        return null;
    }

    private bool InBounds(long offset, long size) => offset >= 0 && size >= 0 && offset + size <= _length;

    private byte[]? ReadBigData(long dbCellData, long totalLength)
    {
        ushort segmentCount = _bytes.ReadUInt16(dbCellData + 0x02);
        uint segmentListOffset = _bytes.ReadUInt32(dbCellData + 0x04);
        long segmentList = CellData(segmentListOffset);

        if (segmentCount <= 0 || segmentList < 0 || !InBounds(segmentList, (long)segmentCount * 4)) { return null; }

        byte[] result = new byte[totalLength];
        long written = 0;

        for (int i = 0; i < segmentCount && written < totalLength; i++)
        {
            uint segmentOffset = _bytes.ReadUInt32(segmentList + (long)i * 4);
            long segment = CellData(segmentOffset);

            if (segment < 0) { return null; }

            // Big-data segments hold up to 16344 payload bytes.
            int chunk = (int)Math.Min(16344, totalLength - written);

            if (!InBounds(segment, chunk)) { return null; }

            _bytes.ReadBytes(segment, result.AsSpan((int)written, chunk));
            written += chunk;
        }

        return written == totalLength ? result : null;
    }

    private object? ReadValue(uint nkCellOffset, string name)
    {
        long nk = CellData(nkCellOffset);

        if (nk < 0 || !InBounds(nk, 0x2C) || _bytes.ReadUInt16(nk) != SigNk) { return null; }

        int valueCount = _bytes.ReadInt32(nk + 0x24);
        uint valueListOffset = _bytes.ReadUInt32(nk + 0x28);

        // Cap declared value counts before scanning vk offsets.
        if (valueCount <= 0 || valueListOffset == InvalidOffset || valueCount > Math.Min(MaxValuesPerNode, _length / 4)) { return null; }

        long valueList = CellData(valueListOffset);

        if (valueList < 0 || !InBounds(valueList, (long)valueCount * 4)) { return null; }

        for (int i = 0; i < valueCount; i++)
        {
            uint vkOffset = _bytes.ReadUInt32(valueList + (long)i * 4);

            if (TryReadValueRecord(vkOffset, name, out object? value)) { return value; }
        }

        return null;
    }

    // Resident values pack up to 4 bytes in the data-offset field; larger values use cells or "db" big data.
    private byte[]? ReadValueData(uint dataLengthRaw, uint dataOffset)
    {
        bool resident = (dataLengthRaw & 0x80000000u) != 0;
        long length = dataLengthRaw & 0x7FFFFFFF;

        if (length < 0 || length > _length) { return null; }

        // Cap before allocation so crafted value lengths cannot crash the elevated helper.
        if (length > MaxValueBytes)
        {
            _logger?.Debug($"{nameof(OfflineHiveFile)}: value data length {length} exceeds the {MaxValueBytes}-byte cap; treating as malformed.");

            return null;
        }

        if (resident)
        {
            int residentLength = (int)Math.Min(length, 4);
            byte[] inline = new byte[residentLength];
            Span<byte> packed = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(packed, dataOffset);
            packed[..residentLength].CopyTo(inline);

            return inline;
        }

        long data = CellData(dataOffset);

        if (data < 0) { return null; }

        // Big data: "db" points at segment cells, each up to 16344 bytes.
        if (length > 16344 && InBounds(data, 8) && _bytes.ReadUInt16(data) == SigDb)
        {
            return ReadBigData(data, length);
        }

        if (!InBounds(data, length)) { return null; }

        byte[] buffer = new byte[length];
        _bytes.ReadBytes(data, buffer);

        return buffer;
    }

    private bool TryReadKeyName(uint nkCellOffset, out string name)
    {
        name = string.Empty;
        long nk = CellData(nkCellOffset);

        if (nk < 0 || !InBounds(nk, 0x50) || _bytes.ReadUInt16(nk) != SigNk) { return false; }

        ushort flags = _bytes.ReadUInt16(nk + 0x02);
        int nameLength = _bytes.ReadUInt16(nk + 0x48);

        if (nameLength <= 0 || !InBounds(nk + 0x4C, nameLength)) { return false; }

        name = DecodeName(nk + 0x4C, nameLength, ascii: (flags & 0x20) != 0);

        return true;
    }

    private bool TryReadValueRecord(uint vkCellOffset, string wantedName, out object? value)
    {
        value = null;
        long vk = CellData(vkCellOffset);

        if (vk < 0 || !InBounds(vk, 0x14) || _bytes.ReadUInt16(vk) != SigVk) { return false; }

        int nameLength = _bytes.ReadUInt16(vk + 0x02);
        uint dataLengthRaw = _bytes.ReadUInt32(vk + 0x04);
        uint dataOffset = _bytes.ReadUInt32(vk + 0x08);
        uint type = _bytes.ReadUInt32(vk + 0x0C);
        ushort vkFlags = _bytes.ReadUInt16(vk + 0x10);

        string vkName;

        if (nameLength <= 0)
        {
            vkName = string.Empty;
        }
        else if (!InBounds(vk + 0x14, nameLength))
        {
            return false;
        }
        else
        {
            vkName = DecodeName(vk + 0x14, nameLength, ascii: (vkFlags & 0x01) != 0);
        }

        if (!string.Equals(vkName, wantedName, StringComparison.OrdinalIgnoreCase)) { return false; }

        byte[]? data = ReadValueData(dataLengthRaw, dataOffset);

        value = data is null ? null : Decode(type, data);

        return true;
    }

    // Walk lf/lh, li, or ri lists with depth, cycle, and entry-budget guards.
    private IEnumerable<uint> WalkSubkeyList(uint listOffset, int depth, HashSet<uint> visitedLists, TraversalBudget budget)
    {
        if (depth > MaxListDepth || !visitedLists.Add(listOffset)) { yield break; }

        long list = CellData(listOffset);

        if (list < 0 || !InBounds(list, 4)) { yield break; }

        ushort signature = _bytes.ReadUInt16(list);
        ushort count = _bytes.ReadUInt16(list + 0x02);

        switch (signature)
        {
            case SigLf:
            case SigLh: // 8 bytes per entry: 4-byte nk offset + 4-byte name hint/hash.
                if (!InBounds(list + 4, (long)count * 8)) { yield break; }

                for (int i = 0; i < count; i++)
                {
                    if (!budget.Charge()) { yield break; }

                    yield return _bytes.ReadUInt32(list + 0x04 + (long)i * 8);
                }

                break;
            case SigLi: // 4 bytes per entry: nk offset.
                if (!InBounds(list + 4, (long)count * 4)) { yield break; }

                for (int i = 0; i < count; i++)
                {
                    if (!budget.Charge()) { yield break; }

                    yield return _bytes.ReadUInt32(list + 0x04 + (long)i * 4);
                }

                break;
            case SigRi: // 4 bytes per entry: sub-list offset.
                if (!InBounds(list + 4, (long)count * 4)) { yield break; }

                for (int i = 0; i < count; i++)
                {
                    if (!budget.Charge()) { yield break; }

                    uint subListOffset = _bytes.ReadUInt32(list + 0x04 + (long)i * 4);

                    foreach (uint childOffset in WalkSubkeyList(subListOffset, depth + 1, visitedLists, budget))
                    {
                        yield return childOffset;
                    }
                }

                break;
        }
    }

    private sealed class ArrayHiveBytes(byte[] data) : IHiveBytes
    {
        public long Length => data.Length;

        public void Dispose() { }

        public void ReadBytes(long offset, Span<byte> destination) => data.AsSpan((int)offset, destination.Length).CopyTo(destination);

        public int ReadInt32(long offset) => BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan((int)offset));

        public ushort ReadUInt16(long offset) => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan((int)offset));

        public uint ReadUInt32(long offset) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan((int)offset));
    }

    private sealed class MappedHiveBytes(MemoryMappedFile map, MemoryMappedViewAccessor view, long length) : IHiveBytes
    {
        public long Length => length;

        public void Dispose()
        {
            view.Dispose();
            map.Dispose();
        }

        public void ReadBytes(long offset, Span<byte> destination)
        {
            byte[] rented = ArrayPool<byte>.Shared.Rent(destination.Length);

            try
            {
                view.ReadArray(offset, rented, 0, destination.Length);
                rented.AsSpan(0, destination.Length).CopyTo(destination);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }

        public int ReadInt32(long offset) => view.ReadInt32(offset);

        public ushort ReadUInt16(long offset) => view.ReadUInt16(offset);

        public uint ReadUInt32(long offset) => view.ReadUInt32(offset);
    }

    // Per-entry charging enforces both per-enumeration and whole-hive traversal caps.
    private sealed class TraversalBudget(OfflineHiveFile hive)
    {
        private long _examinedThisEnumeration;

        public bool Charge()
        {
            if (_examinedThisEnumeration >= MaxEntriesPerEnumeration || hive._hiveTraversalBudget <= 0) { return false; }

            _examinedThisEnumeration++;
            hive._hiveTraversalBudget--;

            return true;
        }
    }
}
