// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Interop;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Provider.Resolution;
using System.Runtime.InteropServices;

namespace EventLogExpert.Eventing.ProviderMetadata;

/// <summary>
///     Reads a provider DLL's RT_MESSAGETABLE. Every walk is bounds-checked against the resource size from
///     <see cref="NativeMethods.SizeofResource" /> so a malformed offset/length can never read outside the mapped
///     resource: CountEntries returns -1 when the table is malformed anywhere (callers treat it as no table), while
///     per-id extraction stops at the first malformed region and returns only the matches from the well-formed prefix,
///     never an access violation. Modules are loaded MUI-aware and freed immediately - no handle is held across calls.
/// </summary>
internal static class MessageTableReader
{
    /// <summary>
    ///     Appends entries to <paramref name="into" /> in walk order (file -> block -> id). When
    ///     <paramref name="shortIdFilter" /> is -1 every entry is appended; otherwise only entries whose unsigned low-16
    ///     ShortId equals the filter (matching the eager store's <c>(ushort)ShortId</c> keying).
    /// </summary>
    internal static void AppendMatches(nint memTable, uint size, string providerName, int shortIdFilter, List<MessageModel> into)
    {
        if (!InBounds(0, 4, size)) { return; }

        int numberOfBlocks = Marshal.ReadInt32(memTable);

        if (numberOfBlocks < 0) { return; }

        int blockOffset = 4;

        for (int block = 0; block < numberOfBlocks; block++)
        {
            if (!InBounds(blockOffset, 12, size)) { return; }

            int lowId = Marshal.ReadInt32(memTable, blockOffset);
            int highId = Marshal.ReadInt32(memTable, blockOffset + 4);
            int entryOffset = Marshal.ReadInt32(memTable, blockOffset + 8);

            if (lowId > highId) { return; }

            for (long id = lowId; id <= highId; id++)
            {
                if (!InBounds(entryOffset, 4, size)) { return; }

                short length = Marshal.ReadInt16(memTable, entryOffset);
                short flags = Marshal.ReadInt16(memTable, entryOffset + 2);

                if (length < 4 || !InBounds(entryOffset, length, size)) { return; }

                if (shortIdFilter < 0 || (ushort)(short)id == shortIdFilter)
                {
                    into.Add(new MessageModel
                    {
                        Text = ReadText(memTable + entryOffset + 4, length - 4, flags),
                        ShortId = (short)id,
                        ProviderName = providerName,
                        RawId = id
                    });
                }

                entryOffset += length;
            }

            blockOffset += 12;
        }
    }

    /// <summary>Counts entries with a fully bounds-checked structural walk (no string materialization). -1 if malformed.</summary>
    internal static int CountEntries(nint memTable, uint size)
    {
        if (!InBounds(0, 4, size)) { return -1; }

        int numberOfBlocks = Marshal.ReadInt32(memTable);

        if (numberOfBlocks < 0) { return -1; }

        int count = 0;
        int blockOffset = 4;

        for (int block = 0; block < numberOfBlocks; block++)
        {
            if (!InBounds(blockOffset, 12, size)) { return -1; }

            int lowId = Marshal.ReadInt32(memTable, blockOffset);
            int highId = Marshal.ReadInt32(memTable, blockOffset + 4);
            int entryOffset = Marshal.ReadInt32(memTable, blockOffset + 8);

            if (lowId > highId) { return -1; }

            for (long id = lowId; id <= highId; id++)
            {
                if (!InBounds(entryOffset, 4, size)) { return -1; }

                short length = Marshal.ReadInt16(memTable, entryOffset);

                if (length < 4 || !InBounds(entryOffset, length, size)) { return -1; }

                count++;
                entryOffset += length;
            }

            blockOffset += 12;
        }

        return count;
    }

    /// <summary>Returns the first entry whose RawId equals <paramref name="rawId" /> (first-wins), or null.</summary>
    internal static MessageModel? FindFirstByRawId(nint memTable, uint size, long rawId, string providerName)
    {
        if (!InBounds(0, 4, size)) { return null; }

        int numberOfBlocks = Marshal.ReadInt32(memTable);

        if (numberOfBlocks < 0) { return null; }

        int blockOffset = 4;

        for (int block = 0; block < numberOfBlocks; block++)
        {
            if (!InBounds(blockOffset, 12, size)) { return null; }

            int lowId = Marshal.ReadInt32(memTable, blockOffset);
            int highId = Marshal.ReadInt32(memTable, blockOffset + 4);
            int entryOffset = Marshal.ReadInt32(memTable, blockOffset + 8);

            if (lowId > highId) { return null; }

            for (long id = lowId; id <= highId; id++)
            {
                if (!InBounds(entryOffset, 4, size)) { return null; }

                short length = Marshal.ReadInt16(memTable, entryOffset);
                short flags = Marshal.ReadInt16(memTable, entryOffset + 2);

                if (length < 4 || !InBounds(entryOffset, length, size)) { return null; }

                if (id == rawId)
                {
                    return new MessageModel
                    {
                        Text = ReadText(memTable + entryOffset + 4, length - 4, flags),
                        ShortId = (short)id,
                        ProviderName = providerName,
                        RawId = id
                    };
                }

                entryOffset += length;
            }

            blockOffset += 12;
        }

        return null;
    }

    internal static bool TryOpen(string file, ITraceLogger? logger, out LibraryHandle handle, out nint memTable, out uint size)
    {
        memTable = nint.Zero;
        size = 0;
        handle = LoadMessageModule(file, logger);

        if (handle.IsInvalid)
        {
            handle.Dispose();

            return false;
        }

        nint info = NativeMethods.FindResourceExA(handle, NativeMethods.RT_MESSAGETABLE, 1);

        if (info == nint.Zero)
        {
            handle.Dispose();

            return false;
        }

        size = NativeMethods.SizeofResource(handle, info);
        nint resource = NativeMethods.LoadResource(handle, info);

        if (resource == nint.Zero || size == 0)
        {
            handle.Dispose();

            return false;
        }

        memTable = NativeMethods.LockResource(resource);

        if (memTable != nint.Zero) { return true; }

        handle.Dispose();

        return false;
    }

    private static bool InBounds(int offset, int length, uint size) =>
        offset >= 0 && length >= 0 && (long)offset + length <= size;

    private static LibraryHandle LoadMessageModule(string file, ITraceLogger? logger)
    {
        file = Environment.ExpandEnvironmentVariables(file);

        const LoadLibraryFlags MuiAwareFlags =
            LoadLibraryFlags.LOAD_LIBRARY_AS_IMAGE_RESOURCE |
            LoadLibraryFlags.LOAD_LIBRARY_AS_DATAFILE_EXCLUSIVE;

        var module = NativeMethods.LoadLibraryExW(file, nint.Zero, MuiAwareFlags);
        int error = Marshal.GetLastWin32Error();

        if (!module.IsInvalid) { return module; }

        module.Dispose();

        string primaryFailure =
            $"LoadLibraryEx failed for {file} with flags LOAD_LIBRARY_AS_IMAGE_RESOURCE | " +
            $"LOAD_LIBRARY_AS_DATAFILE_EXCLUSIVE. Error: {error} ({NativeMethods.FormatSystemMessage((uint)error) ?? "unknown"}).";

        // Legacy fallback: re-attempt with the leaf filename resolved under the trusted system directory, but only for
        // pure leaf inputs (no directory information) so a "subdir\foo.dll" can never be hijacked to a system binary.
        if (!string.Equals(file, Path.GetFileName(file), StringComparison.Ordinal))
        {
            logger?.Debug($"{primaryFailure} Skipping leaf-name fallback because the input contains directory information.");

            return LibraryHandle.Zero;
        }

        string leafName = Path.GetFileName(file);

        if (string.IsNullOrEmpty(leafName))
        {
            logger?.Debug($"{primaryFailure} Skipping leaf-name fallback because no leaf filename could be extracted.");

            return LibraryHandle.Zero;
        }

        string systemPath = Path.Combine(Environment.SystemDirectory, leafName);

        if (!File.Exists(systemPath))
        {
            logger?.Debug($"{primaryFailure} Skipping leaf-name fallback because '{leafName}' does not exist under {Environment.SystemDirectory}.");

            return LibraryHandle.Zero;
        }

        logger?.Debug($"{primaryFailure} Falling back to leaf-name resolution against the system directory: {systemPath}.");

        module = NativeMethods.LoadLibraryExW(systemPath, nint.Zero, MuiAwareFlags);
        error = Marshal.GetLastWin32Error();

        if (!module.IsInvalid) { return module; }

        module.Dispose();

        logger?.Debug(
            $"LoadLibraryEx failed for {systemPath} (leaf-name fallback) with flags LOAD_LIBRARY_AS_IMAGE_RESOURCE | " +
            $"LOAD_LIBRARY_AS_DATAFILE_EXCLUSIVE. Error: {error} ({NativeMethods.FormatSystemMessage((uint)error) ?? "unknown"}). " +
            $"Original requested file was: {file}.");

        return LibraryHandle.Zero;
    }

    // Reads the entry text up to the first null, bounded by the entry length. flags: 1 = Unicode, 0 and 2 = single-byte
    // (2 is undefined but ESE message tables use it for ANSI); any other value keeps the legacy parser's sentinel
    // instead of decoding arbitrary bytes.
    private static string ReadText(nint textPtr, int maxBytes, short flags)
    {
        if (flags is not (0 or 1 or 2)) { return "Error: Bad flags. Could not get text."; }

        if (maxBytes <= 0) { return string.Empty; }

        if (flags == 1)
        {
            int chars = 0;
            int maxChars = maxBytes / 2;
            while (chars < maxChars && Marshal.ReadInt16(textPtr, chars * 2) != 0) { chars++; }

            return Marshal.PtrToStringUni(textPtr, chars);
        }

        int bytes = 0;

        while (bytes < maxBytes && Marshal.ReadByte(textPtr, bytes) != 0) { bytes++; }

        return Marshal.PtrToStringAnsi(textPtr, bytes);
    }
}
