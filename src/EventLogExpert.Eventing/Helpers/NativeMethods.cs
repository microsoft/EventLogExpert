// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Models;
using System.Runtime.InteropServices;

// ReSharper disable InconsistentNaming
// We are defining some win32 types in this file, so we
// are not following the usual C# naming conventions.

namespace EventLogExpert.Eventing.Helpers;

[Flags]
internal enum LoadLibraryFlags : uint
{
    None = 0,
    DONT_RESOLVE_DLL_REFERENCES = 0x00000001,
    LOAD_IGNORE_CODE_AUTHZ_LEVEL = 0x00000010,
    LOAD_LIBRARY_AS_DATAFILE = 0x00000002,
    LOAD_LIBRARY_AS_DATAFILE_EXCLUSIVE = 0x00000040,
    LOAD_LIBRARY_AS_IMAGE_RESOURCE = 0x00000020,
    LOAD_LIBRARY_SEARCH_APPLICATION_DIR = 0x00000200,
    LOAD_LIBRARY_SEARCH_DEFAULT_DIRS = 0x00001000,
    LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR = 0x00000100,
    LOAD_LIBRARY_SEARCH_SYSTEM32 = 0x00000800,
    LOAD_LIBRARY_SEARCH_USER_DIRS = 0x00000400,
    LOAD_WITH_ALTERED_SEARCH_PATH = 0x00000008
}

internal static partial class NativeMethods
{
    internal const int RT_MESSAGETABLE = 11;

    private const uint FORMAT_MESSAGE_FROM_HMODULE = 0x00000800;
    private const uint FORMAT_MESSAGE_FROM_SYSTEM = 0x00001000;
    private const uint FORMAT_MESSAGE_IGNORE_INSERTS = 0x00000200;

    private const string Kernel32Api = "kernel32.dll";

    /// <summary>
    ///     Detects unresolved FormatMessage insert placeholders in the result. Catches positional
    ///     inserts (%1 through %99, including forms like %1!s!) and common printf-style specifiers
    ///     (%s, %d, %p, etc.) that appear in some message tables. Skips escaped percent signs (%%).
    ///     Does not detect compound printf forms like %lu or %I64u.
    /// </summary>
    internal static bool ContainsFormatInsert(ReadOnlySpan<char> text)
    {
        for (int i = 0; i < text.Length - 1; i++)
        {
            if (text[i] != '%') { continue; }

            char next = text[i + 1];

            // %% is an escaped percent literal — skip it
            if (next == '%')
            {
                i++;

                continue;
            }

            // %1-%9 (and %10-%99) are FormatMessage positional inserts
            if (next is >= '1' and <= '9')
            {
                return true;
            }

            // Common printf-style format specifiers found in some message tables
            if (next is 's' or 'S' or 'd' or 'i' or 'u' or 'o'
                or 'x' or 'X' or 'c' or 'C' or 'p'
                or 'e' or 'E' or 'f' or 'F' or 'g' or 'G')
            {
                return true;
            }
        }

        return false;
    }

    [LibraryImport(Kernel32Api, SetLastError = true)]
    internal static partial LibraryHandle FindResourceExA(
        LibraryHandle hModule,
        int lpType,
        int lpName,
        ushort wLanguage = 0);

    [LibraryImport(Kernel32Api, StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    internal static partial int FormatMessageW(
        uint dwFlags,
        IntPtr lpSource,
        uint dwMessageId,
        int dwLanguageId,
        Span<char> lpBuffer,
        int nSize,
        IntPtr arguments);

    /// <summary>Formats an NTSTATUS code to a human-readable string using ntdll.dll's message table only.</summary>
    internal static string? FormatNtStatusMessage(uint ntStatus)
    {
        IntPtr ntdllHandle = GetModuleHandleW("ntdll.dll");

        if (ntdllHandle == IntPtr.Zero) { return null; }

        return FormatMessageFromModule(ntdllHandle, ntStatus);
    }

    /// <summary>Formats an error code to a human-readable string using the system message table.</summary>
    internal static string? FormatSystemMessage(uint errorCode) =>
        FormatMessageWithRetry(
            FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_IGNORE_INSERTS,
            IntPtr.Zero,
            errorCode);

    [LibraryImport(Kernel32Api, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool FreeLibrary(IntPtr hModule);

    [LibraryImport(Kernel32Api, StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    internal static partial IntPtr GetModuleHandleW(string lpModuleName);

    [LibraryImport(Kernel32Api, SetLastError = true)]
    internal static partial LibraryHandle LoadLibraryExW(
        [MarshalAs(UnmanagedType.LPWStr)] string lpFileName,
        IntPtr hReservedNull,
        LoadLibraryFlags dwFlags);

    [LibraryImport(Kernel32Api, SetLastError = true)]
    internal static partial IntPtr LoadResource(LibraryHandle hModule, LibraryHandle hResInfo);

    [LibraryImport(Kernel32Api)]
    internal static partial IntPtr LockResource(IntPtr hResData);

    private static string? FormatMessageFromModule(IntPtr moduleHandle, uint messageId) =>
        FormatMessageWithRetry(
            FORMAT_MESSAGE_FROM_HMODULE | FORMAT_MESSAGE_IGNORE_INSERTS,
            moduleHandle,
            messageId);

    private static string? FormatMessageWithRetry(uint flags, IntPtr source, uint messageId)
    {
        Span<char> stackBuffer = stackalloc char[512];

        int length = FormatMessageW(flags, source, messageId, 0, stackBuffer, stackBuffer.Length, IntPtr.Zero);

        if (length > 0) { return TrimFormatMessageResult(stackBuffer[..length]); }

        if (Marshal.GetLastWin32Error() != Interop.ERROR_INSUFFICIENT_BUFFER) { return null; }

        // Retry with progressively larger pooled buffers
        ReadOnlySpan<int> retrySizes = [2048, 8192, 32768];

        foreach (int size in retrySizes)
        {
            char[] rented = System.Buffers.ArrayPool<char>.Shared.Rent(size);

            try
            {
                length = FormatMessageW(flags, source, messageId, 0, rented, rented.Length, IntPtr.Zero);

                if (length > 0) { return TrimFormatMessageResult(rented.AsSpan(0, length)); }

                if (Marshal.GetLastWin32Error() != Interop.ERROR_INSUFFICIENT_BUFFER) { return null; }
            }
            finally
            {
                System.Buffers.ArrayPool<char>.Shared.Return(rented);
            }
        }

        return null;
    }

    private static string? TrimFormatMessageResult(Span<char> result)
    {
        while (result.Length > 0 && (result[^1] == '\r' || result[^1] == '\n' || result[^1] == ' '))
        {
            result = result[..^1];
        }

        if (result.Length == 0) { return null; }

        // Reject messages that contain unresolved FormatMessage insert placeholders
        // (e.g., "%1", "%1!s!", "%s", "%p"). These occur when FORMAT_MESSAGE_IGNORE_INSERTS is
        // used with messages that expect parameters, producing misleading template text.
        if (ContainsFormatInsert(result)) { return null; }

        return new string(result);
    }
}
