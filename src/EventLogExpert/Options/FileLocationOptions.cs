// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Options;

public class FileLocationOptions
{
    public string DatabasePath => Path.Join(FileSystem.AppDataDirectory, "Databases");

    public string LoggingPath => Path.Join(FileSystem.AppDataDirectory, "debug.log");

    public string SettingsPath => Path.Join(FileSystem.AppDataDirectory, "settings.json");
}
