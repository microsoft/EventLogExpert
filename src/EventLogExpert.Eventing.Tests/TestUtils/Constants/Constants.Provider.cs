// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Eventing.Tests.TestUtils.Constants;

public sealed class Constants
{
    public const string ApplicationLogName = "Application";
    public const string SystemLogName = "System";
    public const string SecurityLogName = "Security";

    public const string KernelGeneralLogName = "Microsoft-Windows-Kernel-General";
    public const string PowerShellLogName = "Microsoft-Windows-PowerShell";
    public const string SecurityAuditingLogName = "Microsoft-Windows-Security-Auditing";
    public const string ServiceControlManagerLogName = "Service Control Manager";

    public const string NonExistentDatabaseFullPath = @"C:\Test\NonExistentDatabase.db";
    public const string NonExistentDll = "NonExistent.dll";
    public const string NonExistentDllFullPath = @"C:\Windows\System32\NonExistent.dll";
    public const string NonExistentDllSystemRootFullPath = @"%SystemRoot%\System32\NonExistent.dll";

    public const string Localhost = "localhost";

    public const string LocalComputer = "LocalComputer";
    public const string RemoteComputer = "RemoteComputer";

    public const string TestProviderLongName = "Microsoft-Windows-EventLogExpert";
    public const string TestProviderName = "EventLogExpert";

    public const string ExchangeFormatedDescription =
        "Database redundancy health check passed.\r\nDatabase copy: SERVER1\r\nRedundancy count: 4\r\nIsSuppressed: False\r\n\r\nErrors:\r\nLots of copy status text";
}
