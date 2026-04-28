// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using Microsoft.Win32;
using System.Text.RegularExpressions;

namespace EventLogExpert.Eventing.Providers;

public partial class RegistryProvider(string? computerName, ITraceLogger? logger = null)
{
    private readonly string? _computerName = computerName;
    private readonly ITraceLogger? _logger = logger;

    /// <summary>Returns the file paths for the message files for this provider.</summary>
    public IEnumerable<string> GetMessageFilesForLegacyProvider(string providerName)
    {
        _logger?.Debug($"GetLegacyProviderFiles called for provider {providerName} on computer {_computerName}");

        // Open an owned base key (do NOT use Registry.LocalMachine — that's a shared static).
        // This makes concurrent calls across instances safe to dispose independently.
        using var hklm = string.IsNullOrEmpty(_computerName)
            ? RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default)
            : RegistryKey.OpenRemoteBaseKey(RegistryHive.LocalMachine, _computerName);

        using var eventLogKey = hklm.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\EventLog") ??
            throw new OpenEventLogRegistryKeyFailedException(_computerName ?? string.Empty);

        foreach (var logSubKeyName in eventLogKey.GetSubKeyNames())
        {
            // Skip Security and State since it requires elevation
            if (logSubKeyName is "Security" or "State")
            {
                continue;
            }

            using var logSubKey = eventLogKey.OpenSubKey(logSubKeyName);
            using var providerSubKey = logSubKey?.OpenSubKey(providerName);

            if (providerSubKey?.GetValue("EventMessageFile") is not string eventMessageFilePath)
            {
                continue;
            }

            _logger?.Debug($"Found message file for legacy provider {providerName} in subkey {providerSubKey.Name}");

            // Filter by extension. The FltMgr provider puts a .sys file in the EventMessageFile value,
            // and trying to load that causes an access violation.
            var supportedExtensions = new[] { ".dll", ".exe" };

            var messageFiles = eventMessageFilePath
                .Split(';')
                .Where(path => supportedExtensions.Contains(Path.GetExtension(path).ToLower()))
                .ToList();

            IEnumerable<string> files;

            if (providerSubKey.GetValue("CategoryMessageFile") is string categoryMessageFilePath)
            {
                var fileList = new List<string> { categoryMessageFilePath };
                fileList.AddRange(messageFiles.Where(f => f != categoryMessageFilePath));
                files = fileList;
            }
            else
            {
                files = messageFiles;
            }

            // Materialize before the using-scopes close the registry handles
            return GetExpandedFilePaths(files).ToList();
        }

        return [];
    }

    [GeneratedRegex("^[A-Z]:")]
    private static partial Regex ConvertRootPath();

    private IEnumerable<string> GetExpandedFilePaths(IEnumerable<string> paths)
    {
        if (string.IsNullOrEmpty(_computerName))
        {
            // For local computer, do it the easy way
            return paths.Select(Environment.ExpandEnvironmentVariables);
        }

        // For remote computer, get SystemRoot from the registry
        // TODO: Support variables other than SystemRoot?
        var systemRoot = GetSystemRoot() ??
            throw new ExpandFilePathsFailedException(
                $"Could not get SystemRoot from remote registry: {_computerName}");

        paths = paths.Select(p =>
        {
            // Expand the variable
            var newPath = p.ReplaceCaseInsensitiveFind("%SystemRoot%", systemRoot);

            // Now replace any drive root references with \\computername\drive$
            var match = ConvertRootPath().Match(newPath);

            if (match.Success)
            {
                newPath = $@"\\{_computerName}\{match.Value[0]}${newPath[2..]}";
            }

            return newPath;
        });

        return paths;
    }

    private string? GetSystemRoot()
    {
        using var hklm = string.IsNullOrEmpty(_computerName)
            ? RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default)
            : RegistryKey.OpenRemoteBaseKey(RegistryHive.LocalMachine, _computerName);

        using var currentVersion = hklm.OpenSubKey(@"Software\Microsoft\Windows NT\CurrentVersion");

        return currentVersion?.GetValue("SystemRoot") as string;
    }

    private class ExpandFilePathsFailedException(string msg) : Exception(msg) {}

    private class OpenEventLogRegistryKeyFailedException(string msg) : Exception(msg) {}
}
