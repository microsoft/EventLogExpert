// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Eventing.Tests.TestUtils.Constants;

public sealed partial class Constants
{
    public const string KernelGeneralLogName = "Microsoft-Windows-Kernel-General";
    public const string PowerShellLogName = "Microsoft-Windows-PowerShell";
    public const string SecurityAuditingLogName = "Microsoft-Windows-Security-Auditing";

    public const string NonExistentDll = "NonExistent.dll";
    public const string NonExistentDllFullPath = @"C:\Windows\System32\NonExistent.dll";
    public const string NonExistentDllSystemRootFullPath = @"%SystemRoot%\System32\NonExistent.dll";

    public const string LocalComputer = "LocalComputer";
    public const string RemoteComputer = "RemoteComputer";

    public const string TestProviderLongName = "Microsoft-Windows-EventLogExpert";
    public const string TestProviderName = "EventLogExpert";
}
