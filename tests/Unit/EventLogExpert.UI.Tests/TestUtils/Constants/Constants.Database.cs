// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Tests.TestUtils.Constants;

public sealed partial class Constants
{
    // Database names
    public const string DatabaseA = "Database A";
    public const string DatabaseB = "Database B";
    public const string DatabaseC = "Database C";

    // Disabled database names
    public const string DisabledDb = "DisabledDb";
    public const string DisabledDb1 = "DisabledDb1";
    public const string DisabledDb2 = "DisabledDb2";
    public const string InitialDisabled = "InitialDisabled";
    public const string NewDisabled1 = "NewDisabled1";
    public const string NewDisabled2 = "NewDisabled2";

    // Versioned database names
    public const string Windows9 = "Windows 9";
    public const string Windows10 = "Windows 10";
    public const string Windows11 = "Windows 11";
    public const string Linux1 = "Linux 1";
    public const string Server1 = "Server 1";
    public const string Server2 = "Server 2";
    public const string Server10 = "Server 10";
    public const string Server20 = "Server 20";

    // Non-versioned database names
    public const string SimpleDatabase = "SimpleDatabase";
    public const string AnotherDb = "AnotherDb";

    // Test database file names (EnabledDatabaseCollectionProviderTests)
    public const string TestDb1 = "TestDb1.db";
    public const string TestDb2 = "TestDb2.db";
    public const string TestDb3 = "TestDb3.db";

    // Test database full paths
    public const string TestDbPath1 = @"C:\Databases\TestDb1.db";
    public const string TestDbPath2 = @"C:\Databases\TestDb2.db";
    public const string TestDbPath3 = @"C:\Databases\TestDb3.db";
}
