// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Tests.TestUtils.Constants;

public sealed partial class Constants
{
    // Log names
    public const string LogNameApplication = "Application";
    public const string LogNameTestLog = "TestLog";
    public const string LogNameLog1 = "Log1";
    public const string LogNameLog2 = "Log2";
    public const string LogNameLog3 = "Log3";
    public const string LogNameNewLog = "NewLog";

    // File paths
    public const string FilePathTestEvtx = @"C:\Logs\test.evtx";

    // Max events
    public const int MaxNewEvents = 1000;
}
