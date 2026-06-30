// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Buffers;
using System.Runtime.InteropServices;

// ReSharper disable InconsistentNaming
namespace EventLogExpert.Eventing.Interop;

internal static partial class NativeMethods
{
    internal const int RT_MESSAGETABLE = 11;

    private const uint FORMAT_MESSAGE_FROM_HMODULE = 0x00000800;
    private const uint FORMAT_MESSAGE_FROM_SYSTEM = 0x00001000;
    private const uint FORMAT_MESSAGE_IGNORE_INSERTS = 0x00000200;

    private const string Kernel32Api = "kernel32.dll";

    // Detects unresolved FormatMessage placeholders; compound printf forms are left as text.
    internal static bool ContainsFormatInsert(ReadOnlySpan<char> text)
    {
        for (int i = 0; i < text.Length - 1; i++)
        {
            if (text[i] != '%') { continue; }

            char next = text[i + 1];

            if (next == '%')
            {
                i++;

                continue;
            }

            if (next is >= '1' and <= '9')
            {
                return true;
            }

            if (next is 's' or 'S' or 'd' or 'i' or 'u' or 'o'
                or 'x' or 'X' or 'c' or 'C' or 'p'
                or 'e' or 'E' or 'f' or 'F' or 'g' or 'G')
            {
                return true;
            }
        }

        return false;
    }

    [LibraryImport(Kernel32Api, EntryPoint = "CloseHandle", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseHandle(IntPtr handle);

    // HRSRC is a non-owning module-resource pointer; do not wrap it in LibraryHandle.
    [LibraryImport(Kernel32Api, SetLastError = true)]
    internal static partial IntPtr FindResourceExA(
        LibraryHandle hModule,
        int lpType,
        int lpName,
        ushort wLanguage = 0);

    // String resource lookup also returns a non-owning HRSRC.
    [LibraryImport(Kernel32Api, StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    internal static partial IntPtr FindResourceW(
        LibraryHandle hModule,
        string lpName,
        string lpType);

    [LibraryImport(Kernel32Api, StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    internal static partial int FormatMessageW(
        uint dwFlags,
        IntPtr lpSource,
        uint dwMessageId,
        int dwLanguageId,
        Span<char> lpBuffer,
        int nSize,
        IntPtr arguments);

    internal static string? FormatNtStatusMessage(uint ntStatus)
    {
        IntPtr ntdllHandle = GetModuleHandleW("ntdll.dll");

        if (ntdllHandle == IntPtr.Zero) { return null; }

        return FormatMessageFromModule(ntdllHandle, ntStatus);
    }

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
    internal static partial IntPtr LoadResource(LibraryHandle hModule, IntPtr hResInfo);

    [LibraryImport(Kernel32Api)]
    internal static partial IntPtr LockResource(IntPtr hResData);

    [LibraryImport(Kernel32Api, SetLastError = true)]
    internal static partial uint SizeofResource(LibraryHandle hModule, IntPtr hResInfo);

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

        if (Marshal.GetLastWin32Error() != Win32ErrorCodes.ERROR_INSUFFICIENT_BUFFER) { return null; }

        ReadOnlySpan<int> retrySizes = [2048, 8192, 32768];

        foreach (int size in retrySizes)
        {
            char[] rented = ArrayPool<char>.Shared.Rent(size);

            try
            {
                length = FormatMessageW(flags, source, messageId, 0, rented, rented.Length, IntPtr.Zero);

                if (length > 0) { return TrimFormatMessageResult(rented.AsSpan(0, length)); }

                if (Marshal.GetLastWin32Error() != Win32ErrorCodes.ERROR_INSUFFICIENT_BUFFER) { return null; }
            }
            finally
            {
                ArrayPool<char>.Shared.Return(rented);
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

        // Ignore unresolved FormatMessage templates so placeholders are not returned as final text.
        return ContainsFormatInsert(result) ? null : new string(result);
    }
}
