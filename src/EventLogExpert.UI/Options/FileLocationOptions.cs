// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Options;

public class FileLocationOptions(string basePath)
{
    private readonly string _basePath = basePath;

    public string DatabasePath => Path.Join(_basePath, "Databases");

    public string LoggingPath => Path.Join(_basePath, "debug.log");

    public string SettingsPath => Path.Join(_basePath, "settings.json");
}
