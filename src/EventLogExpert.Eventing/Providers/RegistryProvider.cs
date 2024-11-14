// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using Microsoft.Win32;
using System.Text.RegularExpressions;

namespace EventLogExpert.Eventing.Providers;

public partial class RegistryProvider(string? computerName, ITraceLogger? logger = null)
{
    private readonly ITraceLogger? _logger = logger;

    public string? ComputerName { get; } = computerName;

    /// <summary>sounds Returns the file paths for the message files for this provider.</summary>
    public IEnumerable<string> GetMessageFilesForLegacyProvider(string providerName)
    {
        _logger?.Trace($"GetLegacyProviderFiles called for provider {providerName} on computer {ComputerName}");

        var hklm = string.IsNullOrEmpty(ComputerName)
            ? Registry.LocalMachine
            : RegistryKey.OpenRemoteBaseKey(RegistryHive.LocalMachine, ComputerName);

        var eventLogKey = hklm.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\EventLog");

        if (eventLogKey == null)
        {
            throw new OpenEventLogRegistryKeyFailedException(ComputerName ?? string.Empty);
        }

        foreach (var logSubKeyName in eventLogKey.GetSubKeyNames())
        {
            // Skip Security and State since it requires elevation
            if (logSubKeyName is "Security" or "State")
            {
                continue;
            }

            var logSubKey = eventLogKey.OpenSubKey(logSubKeyName);
            var providerSubKey = logSubKey?.OpenSubKey(providerName);

            if (providerSubKey?.GetValue("EventMessageFile") is not string eventMessageFilePath)
            {
                continue;
            }

            _logger?.Trace($"Found message file for legacy provider {providerName} in subkey {providerSubKey.Name}");

            var categoryMessageFilePath = providerSubKey.GetValue("CategoryMessageFile") as string;

            // Filter by extension. The FltMgr provider puts a .sys file in the EventMessageFile value,
            // and trying to load that causes an access violation.
            var supportedExtensions = new[] { ".dll", ".exe" };

            var messageFiles = eventMessageFilePath
                .Split(';')
                .Where(path => supportedExtensions.Contains(Path.GetExtension(path).ToLower()))
                .ToList();

            IEnumerable<string> files;

            if (categoryMessageFilePath != null)
            {
                var fileList = new List<string> { categoryMessageFilePath };
                fileList.AddRange(messageFiles.Where(f => f != categoryMessageFilePath));
                files = fileList;
            }
            else
            {
                files = messageFiles;
            }

            // Now we have all our paths, but they are not expanded yet, so expand them
            files = GetExpandedFilePaths(files).ToList();

            hklm.Close();
            return files;
        }

        hklm.Close();
        return new List<string>();
    }

    public string? GetSystemRoot()
    {
        var hklm = string.IsNullOrEmpty(ComputerName)
            ? Registry.LocalMachine
            : RegistryKey.OpenRemoteBaseKey(RegistryHive.LocalMachine, ComputerName);

        var currentVersion = hklm.OpenSubKey(@"Software\Microsoft\Windows NT\CurrentVersion");
        var systemRoot = currentVersion?.GetValue("SystemRoot") as string;

        return systemRoot;
    }

    [GeneratedRegex("^[A-Z]:")]
    private static partial Regex ConvertRootPath();

    private IEnumerable<string> GetExpandedFilePaths(IEnumerable<string> paths)
    {
        if (string.IsNullOrEmpty(ComputerName))
        {
            // For local computer, do it the easy way
            return paths.Select(Environment.ExpandEnvironmentVariables);
        }

        // For remote computer, get SystemRoot from the registry
        // TODO: Support variables other than SystemRoot?
        var systemRoot = GetSystemRoot();

        if (systemRoot == null)
        {
            throw new ExpandFilePathsFailedException(
                $"Could not get SystemRoot from remote registry: {ComputerName}");
        }

        paths = paths.Select(p =>
        {
            // Expand the variable
            var newPath = p.ReplaceCaseInsensitiveFind("%SystemRoot%", systemRoot);

            // Now replace any drive root references with \\computername\drive$
            var match = ConvertRootPath().Match(newPath);

            if (match.Success)
            {
                newPath = $@"\\{ComputerName}\{match.Value[0]}${newPath[2..]}";
            }

            return newPath;
        });

        return paths;
    }

    private class ExpandFilePathsFailedException(string msg) : Exception(msg) {}

    private class OpenEventLogRegistryKeyFailedException(string msg) : Exception(msg) {}
}
