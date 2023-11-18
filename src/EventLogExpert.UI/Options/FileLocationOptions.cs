// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Options;

public sealed class FileLocationOptions(string basePath)
{
    public string DatabasePath => Path.Join(basePath, "Databases");

    public string LoggingPath => Path.Join(basePath, "debug.log");

    public string SettingsPath => Path.Join(basePath, "settings.json");
}
