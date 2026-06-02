// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Windows.Tests.TestUtils.Constants;

public static partial class Constants
{
    public const string FirstArg = "a.evtx";
    public const string LastArg = "c.evtx";
    public const string MiddleArgWithSpaces = "B with space.evtx";
    public const string MultipleArgsCommandLine = @"a.evtx ""B with space.evtx"" c.evtx";
    public const string PathWithSpacesUnquoted = @"C:\Users\Jane Doe\Logs\sample.evtx";
    public const string QuotedDriveRoot = @"""C:""";
    public const string QuotedPathWithSpaces = @"""C:\Users\Jane Doe\Logs\sample.evtx""";
    public const string QuotedUncPath = @"""\\server\share\sample.evtx""";
    public const string UnquotedCLogsSampleEvtx = @"C:\Logs\sample.evtx";
    public const string UnquotedDriveRoot = "C:";
    public const string UnquotedUncPath = @"\\server\share\sample.evtx";
}
