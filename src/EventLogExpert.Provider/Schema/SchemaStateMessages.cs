// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Provider.Schema;

/// <summary>Shared user-facing schema-state error messages.</summary>
public static class SchemaStateMessages
{
    public const string DefaultLabel = "Database";
    public const string SourceLabel = "Source database";
    public const string TargetLabel = "Target database";

    public static string UnrecognizedSchema(string label, string path) =>
        $"{label} '{path}' has an unrecognized schema. The file may be corrupt or from a newer or incompatible version of EventLogExpert. Delete or replace the file.";

    public static string UnsupportedV1OrV2Schema(string path, int currentVersion) =>
        $"Database '{path}' is at schema v{currentVersion}; this version is no longer supported. Upgrade through an older EventLogExpert release that supports v3 first, or delete the file.";
}
