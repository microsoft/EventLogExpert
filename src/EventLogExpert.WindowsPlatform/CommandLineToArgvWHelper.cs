// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Runtime.InteropServices;

namespace EventLogExpert.WindowsPlatform;

/// <summary>
///     P/Invoke wrapper around Windows' <c>shell32!CommandLineToArgvW</c>; recovers argv-style tokens from
///     <c>ILaunchActivatedEventArgs.Arguments</c> and <c>ICommandLineActivatedEventArgs.Operation.Arguments</c> (each
///     delivered as a single unparsed string).
/// </summary>
public static partial class CommandLineToArgvWHelper
{
    public static IReadOnlyList<string> Parse(string commandLine)
    {
        if (string.IsNullOrEmpty(commandLine))
        {
            return [];
        }

        nint argv = CommandLineToArgvW(commandLine, out int count);

        if (argv == 0)
        {
            return [];
        }

        try
        {
            if (count <= 0)
            {
                return [];
            }

            var result = new string[count];

            for (int i = 0; i < count; i++)
            {
                nint pStr = Marshal.ReadIntPtr(argv, i * IntPtr.Size);
                result[i] = Marshal.PtrToStringUni(pStr) ?? string.Empty;
            }

            return result;
        }
        finally
        {
            _ = LocalFree(argv);
        }
    }

    [LibraryImport("shell32.dll", EntryPoint = "CommandLineToArgvW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial nint CommandLineToArgvW(string lpCmdLine, out int pNumArgs);

    [LibraryImport("kernel32.dll")]
    private static partial nint LocalFree(nint hMem);
}
