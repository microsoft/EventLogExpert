// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using System.Buffers.Binary;
using System.IO.MemoryMappedFiles;
using System.Text;

namespace EventLogExpert.Eventing.PublisherMetadata.Offline;

/// <summary>
///     Read-only managed parser for a Windows registry hive (<c>regf</c>) file, exposing the root key as an
///     <see cref="IOfflineRegistryKey" />. Reads the hive's bytes directly (memory-mapped) instead of loading it through
///     the Windows registry APIs, so it works regardless of MSIX package identity / registry virtualization and never
///     mounts a hive under <c>HKLM</c>. The hive is treated as hostile, attacker-controlled image content: every offset is
///     bounds-checked in 64-bit math against the actual file length, structure is validated, counts and recursion are
///     capped, and any malformed structure degrades to <see langword="null" />/empty rather than throwing. Log replay of
///     the dual <c>.LOG1</c>/<c>.LOG2</c> sidecars is intentionally NOT performed: a dirty hive (its base-block sequence
///     numbers disagree) is read at its last-flushed state with a warning. The mapping is released on
///     <see cref="Dispose" />.
/// </summary>
internal sealed class OfflineHiveFile : IOfflineRegistryKey
{
    private const int BaseBlockSize = 0x1000;       // hive bins start here; cell offsets are relative to this.
    // far above any real hive's total but bounding a crafted hive's work.
    private const uint InvalidOffset = 0xFFFFFFFF;  // hive sentinel for "no cell".
    private const long MaxEntriesPerEnumeration = 100_000; // per single subkey enumeration: bounds one names list and the
    private const long MaxHiveTraversalEntries = 16_000_000; // whole-hive ceiling on list entries EXAMINED (not just yielded),
    // length cannot drive an unbounded (OutOfMemory) allocation.
    private const int MaxListDepth = 16;     // ri -> li/lf/lh nesting is shallow; cap to stop a cyclic/deep list.
    private const long MaxNameBytes = 0x400; // registry key/value names are <= 255 chars; cap at 1 KB to bound the
    // per-name allocation a crafted nameLength could otherwise request.
    private const long MaxValueBytes = 16 * 1024 * 1024; // a single value is realistically < 1 MB; cap hard so a crafted
    // entries a single (possibly ri-amplified) node may examine.
    private const long MaxValuesPerNode = 100_000;  // per single value lookup: bounds the vk offsets scanned for a name.
    private const ushort SigDb = 0x6264;            // "db" (big data)
    private const uint SigHbin = 0x6e696268;        // "hbin" (hive bin header)
    private const ushort SigLf = 0x666c;            // "lf"
    private const ushort SigLh = 0x686c;            // "lh"
    private const ushort SigLi = 0x696c;            // "li"
    private const ushort SigNk = 0x6b6e;            // "nk"
    private const ushort SigRi = 0x6972;            // "ri"
    private const ushort SigVk = 0x6b76;            // "vk"
    private readonly long _binsEnd;                 // end of the last validated, contiguous hive bin; cells must fall below this.

    private readonly IHiveBytes _bytes;
    private readonly long _length;
    private readonly ITraceLogger? _logger;
    private readonly uint _rootCellOffset;

    private bool _disposed;
    private long _hiveTraversalBudget;              // remaining whole-hive list-entry budget; see EnumerateSubkeyOffsets.

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

    /// <summary>Abstracts the hive's bytes so the parser runs over a memory-mapped file (production) or an array (tests).</summary>
    private interface IHiveBytes : IDisposable
    {
        long Length { get; }

        void ReadBytes(long offset, Span<byte> destination);

        int ReadInt32(long offset);

        ushort ReadUInt16(long offset);

        uint ReadUInt32(long offset);
    }

    /// <summary>The hive's base-block sequence numbers disagree: it was not cleanly flushed when captured.</summary>
    public bool IsDirty { get; }

    /// <summary>
    ///     Opens <paramref name="hiveFilePath" /> as a read-only memory-mapped <c>regf</c> hive, or returns
    ///     <see langword="null" /> (logging the reason) when the file is missing, too small to be a hive, inaccessible (e.g.
    ///     NTFS ACLs require administrator), or not a valid hive.
    /// </summary>
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

        // A real hive is at least one base block plus one hive bin. Guard before CreateFromFile so a 0-byte file (which
        // throws ArgumentException for an empty mapping) or a truncated stub surfaces as "not a hive", not an exception.
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

    /// <summary>Opens an in-memory hive image (used by tests and for already-buffered hives).</summary>
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

    // Resolves a backslash-separated relative path from the nk at fromCellOffset, returning a lightweight key cursor (or
    // null if any segment is absent). Case-insensitive, matching registry semantics.
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

    // Walks the contiguous hive bins from 0x1000, validating each "hbin" header + 4 KB-aligned size, and returns the byte
    // just past the last valid bin. Cells are only honored below this bound, so a crafted cell offset that points into the
    // base block, a torn bin, or padding past the bins degrades to "absent" instead of being read.
    private static long ComputeBinsExtent(IHiveBytes bytes)
    {
        long offset = BaseBlockSize;
        long length = bytes.Length;

        while (offset + 0x20 <= length)
        {
            if (bytes.ReadUInt32(offset) != SigHbin) { break; }

            uint binSize = bytes.ReadUInt32(offset + 0x08);

            if (binSize < BaseBlockSize || (binSize & 0xFFF) != 0) { break; } // bins are non-empty 4 KB-aligned blocks.

            long next = offset + binSize;

            if (next <= offset || next > length) { break; }

            offset = next;
        }

        return offset;
    }

    private static object? Decode(uint type, byte[] data) => type switch
    {
        1 or 2 => DecodeString(data),                                            // REG_SZ / REG_EXPAND_SZ (literal)
        4 => data.Length >= 4 ? BinaryPrimitives.ReadInt32LittleEndian(data) : 0, // REG_DWORD -> Int32 (parity)
        5 => data.Length >= 4 ? BinaryPrimitives.ReadInt32BigEndian(data) : 0,    // REG_DWORD_BIG_ENDIAN -> Int32
        11 => data.Length >= 8 ? BinaryPrimitives.ReadInt64LittleEndian(data) : 0L, // REG_QWORD -> Int64
        7 => DecodeMultiString(data),                                            // REG_MULTI_SZ -> string[]
        _ => data                                                                 // REG_NONE / REG_BINARY / other
    };

    // REG_MULTI_SZ: NUL-delimited UTF-16 strings, double-NUL terminated. Matches Microsoft.Win32.RegistryKey.GetValue
    // EXACTLY: split on NUL, PRESERVE interior empty elements, and drop ONLY the single final terminator segment (not the
    // whole trailing NUL run). So `a\0\0b\0\0` -> ["a","","b"], `a\0\0\0` (["a",""]) -> ["a",""], and zero-length data -> [].
    private static string[] DecodeMultiString(byte[] data)
    {
        int length = data.Length & ~1;

        if (length == 0) { return []; }

        string decoded = Encoding.Unicode.GetString(data, 0, length);
        int end = decoded.Length;

        if (decoded[end - 1] == '\0') { end--; } // drop exactly the one final terminator char, not the whole run.

        string[] segments = decoded[..end].Split('\0');

        // A bare terminator (or trailing terminator char above) leaves a single empty final segment; remove just that one.
        return segments is [.., ""] ? segments[..^1] : segments;
    }

    private static string DecodeString(byte[] data)
    {
        int length = data.Length & ~1; // whole UTF-16 code units only.

        if (length >= 2 && data[length - 1] == 0 && data[length - 2] == 0) { length -= 2; } // strip one trailing NUL.

        return Encoding.Unicode.GetString(data, 0, length);
    }

    // XOR of the 127 little-endian uint32s preceding the stored checksum at 0x1FC. Windows normalizes a computed 0 to 1 and
    // 0xFFFFFFFF to 0xFFFFFFFE before storing, so accept those equivalences rather than reporting a false mismatch.
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
            // A valid hive always carries a full 0x1000-byte base block; anything smaller cannot be one.
            if (bytes.Length < BaseBlockSize)
            {
                logger?.Debug($"{nameof(OfflineHiveFile)}: file too small ({bytes.Length} bytes) to contain a base block.");
                bytes.Dispose();

                return null;
            }

            // Base block: "regf" signature; primary/secondary sequence numbers + the XOR checksum classify clean vs dirty;
            // root cell @0x24.
            if (bytes.ReadUInt32(0) != 0x66676572u) // "regf"
            {
                logger?.Debug($"{nameof(OfflineHiveFile)}: not a regf hive (bad signature).");
                bytes.Dispose();

                return null;
            }

            uint primarySeq = bytes.ReadUInt32(0x04);
            uint secondarySeq = bytes.ReadUInt32(0x08);
            uint rootCellOffset = bytes.ReadUInt32(0x24);

            // The base-block checksum is a XOR of the first 508 bytes (stored at 0x1FC). A mismatch means a torn base, which
            // is classified exactly like a sequence-number mismatch: read the last-flushed state with a warning, never reject.
            bool isDirty = primarySeq != secondarySeq || !IsBaseChecksumValid(bytes);

            // Root cell must land inside the hive bins.
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

    // Absolute byte offset of a cell's DATA (past the 4-byte cell size). Validates that the cell lands inside a declared hbin
    // (below _binsEnd), is ALLOCATED (negative size), and does not overrun the bins. Returns -1 otherwise, so callers treat a
    // sentinel/free/out-of-range cell as "absent". The per-record reads below still bounds-check their own spans.
    private long CellData(uint cellOffset)
    {
        if (cellOffset == InvalidOffset) { return -1; }

        long cellStart = BaseBlockSize + (long)cellOffset;

        if (cellStart < BaseBlockSize || cellStart + 4 > _binsEnd) { return -1; }

        int cellSize = _bytes.ReadInt32(cellStart);

        if (cellSize >= 0) { return -1; } // a free (or zero-length) cell is not a valid record; only allocated cells are read.

        long cellLength = -(long)cellSize;

        if (cellLength < 8 || cellStart + cellLength > _binsEnd) { return -1; } // the cell must fit within the validated bins.

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

        // A node cannot hold more subkeys than the file could physically describe, and never more than the per-enumeration
        // cap; reject an absurd declared count before doing any work.
        if (subkeyCount > Math.Min(MaxEntriesPerEnumeration, _length / 8))
        {
            _logger?.Debug($"{nameof(OfflineHiveFile)}: subkey count {subkeyCount} exceeds the per-enumeration cap; treating as malformed.");

            yield break;
        }

        var visitedLists = new HashSet<uint>();
        var yieldedChildren = new HashSet<uint>();
        var budget = new TraversalBudget(this);

        // An ri root-index fans out across many separately-bounded leaves, so the depth + per-list cycle guards alone cannot
        // bound TOTAL work. The budget charges every list ENTRY examined (even ri entries that yield no child), capping both
        // this single enumeration and the whole hive; de-duping child offsets bounds the names read - a crafted list that
        // points every entry at the SAME large-named nk is then read once, not millions of times.
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

            // Each big-data segment holds up to 16344 payload bytes; clamp to what remains.
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

        // Reject an absurd value count before scanning: a node holds far fewer than the per-node cap, and never more vk
        // pointers than the file could physically describe.
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

    // Reads a value's raw bytes: resident (high bit of the length set => up to 4 bytes packed into the data-offset field)
    // or referenced via a data cell, including big-data ("db") reassembly for values larger than a single cell (16344 B).
    private byte[]? ReadValueData(uint dataLengthRaw, uint dataOffset)
    {
        bool resident = (dataLengthRaw & 0x80000000u) != 0;
        long length = dataLengthRaw & 0x7FFFFFFF;

        if (length < 0 || length > _length) { return null; }

        // Hard cap before any allocation: a single registry value is realistically < 1 MB, so a length larger than the cap
        // is a crafted/corrupt value. Reject it rather than let `new byte[length]` (here or in ReadBigData) drive an
        // OutOfMemoryException, which is intentionally NOT caught and would crash the elevated helper.
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

        // Big data: a "db" indirection record (4-byte header read here, then a 4-byte segment-list offset) points at a list
        // of segment cells, each up to 16344 bytes.
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

    // Walks an lf/lh (fast/hash leaf), li (index leaf), or ri (root index of sub-lists, recursive). Depth-, cycle-, and
    // budget-guarded: every entry examined is charged so a malformed/cyclic/amplified list cannot loop, recurse, or fan out
    // without bound - including ri entries that recurse but yield no children.
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
            case SigLh: // 8 bytes per entry: 4-byte nk offset + 4-byte name hint / hash.
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
            case SigRi: // 4 bytes per entry: offset to a sub-list (recurse).
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
            // MemoryMappedViewAccessor has no Span read; ReadArray fills a byte[] then copies into the span.
            byte[] temp = new byte[destination.Length];
            view.ReadArray(offset, temp, 0, temp.Length);
            temp.CopyTo(destination);
        }

        public int ReadInt32(long offset) => view.ReadInt32(offset);

        public ushort ReadUInt16(long offset) => view.ReadUInt16(offset);

        public uint ReadUInt32(long offset) => view.ReadUInt32(offset);
    }

    // Charges each list entry examined during a SINGLE subkey enumeration against two ceilings: a per-enumeration cap (bounds
    // one names list and a single ri-amplified node) and the whole-hive budget (bounds repeated lookups across the reader).
    // Returns false once either is exhausted so the walk stops promptly. Charging per ENTRY (not per yielded child) is what
    // bounds an ri fan-out whose sub-lists recurse heavily but yield few children.
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
