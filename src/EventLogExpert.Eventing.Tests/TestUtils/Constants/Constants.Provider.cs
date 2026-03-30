// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Eventing.Tests.TestUtils.Constants;

public sealed class Constants
{
    public const string NonExistentDll = "NonExistent.dll";
    public const string NonExistentDllFullPath = @"C:\Windows\System32\NonExistent.dll";
    public const string NonExistentDllSystemRootFullPath = @"%SystemRoot%\System32\NonExistent.dll";

    public const string LocalComputer = "LocalComputer";
    public const string RemoteComputer = "RemoteComputer";

    public const string TestProviderLongName = "Microsoft-Windows-EventLogExpert";
    public const string TestProviderName = "EventLogExpert";
}
