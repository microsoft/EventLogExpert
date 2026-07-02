// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Logging.Abstractions;

public static class LogCategories
{
    public const string App = "App";

    public const string Database = "Database";

    public const string DatabaseTools = "DatabaseTools";

    public const string DatabaseToolsCreate = "DatabaseTools.Create";

    public const string DatabaseToolsDiff = "DatabaseTools.Diff";

    public const string DatabaseToolsMerge = "DatabaseTools.Merge";

    public const string DatabaseToolsShow = "DatabaseTools.Show";

    public const string DatabaseToolsUpgrade = "DatabaseTools.Upgrade";

    public const string Elevation = "Elevation";

    public const string ElevationIpc = "Elevation.Ipc";

    public const string EventLog = "EventLog";

    public const string Offline = "Offline";

    public const string OfflineHive = "Offline.Hive";

    public const string OfflineIso = "Offline.Iso";

    public const string OfflineProviders = "Offline.Providers";

    public const string OfflineVhdx = "Offline.Vhdx";

    public const string OfflineWim = "Offline.Wim";

    public const string Resolution = "Resolution";

    public const string ResolutionDescription = "Resolution.Description";

    public const string ResolutionModern = "Resolution.Modern";

    public const string ResolutionProviders = "Resolution.Providers";

    public const string ResolutionTasks = "Resolution.Tasks";

    public static readonly IReadOnlyList<string> KnownRoots =
        [App, Database, DatabaseTools, Elevation, EventLog, Offline, Resolution];
}
