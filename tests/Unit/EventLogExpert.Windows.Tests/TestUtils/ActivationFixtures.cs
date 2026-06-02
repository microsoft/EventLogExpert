// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using static EventLogExpert.Windows.Tests.TestUtils.Constants.Constants;

namespace EventLogExpert.Windows.Tests.TestUtils;

internal static class ActivationFixtures
{
    public static readonly Func<string, bool> AlwaysFileExists = static _ => true;
    public static readonly Func<string, bool> NeverDirExists = static _ => false;
    public static readonly Func<string, bool> NeverFileExists = static _ => false;

    public static Func<string, bool> AcceptOnlyEvtxFiles() =>
        CreateFileExistsForExtensions(EvtxExtension);

    public static Func<string, bool> CreateDirExistsForPath(string expected) =>
        path => string.Equals(path, expected, StringComparison.OrdinalIgnoreCase);

    public static Func<string, bool> CreateFileExistsForExtensions(params string[] extensions) =>
        path => extensions.Any(ext => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase));

    public static Func<string, bool> CreateFileExistsForPath(string expected) =>
        path => string.Equals(path, expected, StringComparison.OrdinalIgnoreCase);

    public static Func<string, bool>
        CreateFileExistsThatThrowsFor(string throwForPath, Exception toThrow, string acceptForPath) =>
        path => string.Equals(path, throwForPath, StringComparison.OrdinalIgnoreCase) ?
            throw toThrow :
            string.Equals(path, acceptForPath, StringComparison.OrdinalIgnoreCase);
}
