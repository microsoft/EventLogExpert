// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using Microsoft.Win32;

namespace EventLogExpert.Eventing.Providers;

public class RegistryProvider(ITraceLogger? logger = null)
{
    private readonly ITraceLogger? _logger = logger;

    /// <summary>Returns the file paths for the message files for this provider on the local machine.</summary>
    /// <remarks>
    ///     EventLogExpert is a local-only tool. Remote-machine resolution is intentionally not
    ///     supported because the modern provider metadata path (used as a fallback when no legacy
    ///     registry entry exists) is local-only, and silently mixing local and remote sources
    ///     produced wrong message text. Callers must already be operating in a local context.
    /// </remarks>
    public IEnumerable<string> GetMessageFilesForLegacyProvider(string providerName)
    {
        _logger?.Debug($"{nameof(GetMessageFilesForLegacyProvider)} called for provider {providerName}");

        // Open an owned base key (do NOT use Registry.LocalMachine — that's a shared static).
        // This makes concurrent calls across instances safe to dispose independently.
        using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default);

        const string EventLogKeyPath = @"SYSTEM\CurrentControlSet\Services\EventLog";

        using var eventLogKey = hklm.OpenSubKey(EventLogKeyPath) ??
            throw new OpenEventLogRegistryKeyFailedException(
                $@"Failed to open HKEY_LOCAL_MACHINE\{EventLogKeyPath}.");

        foreach (var logSubKeyName in eventLogKey.GetSubKeyNames())
        {
            // Skip Security and State since it requires elevation
            if (LogNames.AdminOnlyLiveLogNames.Contains(logSubKeyName))
            {
                continue;
            }

            using var logSubKey = eventLogKey.OpenSubKey(logSubKeyName);
            using var providerSubKey = logSubKey?.OpenSubKey(providerName);

            if (providerSubKey is null)
            {
                continue;
            }

            if (providerSubKey.GetValue("EventMessageFile") is not string eventMessageFilePath)
            {
                _logger?.Debug(
                    $"Legacy provider registry subkey found without EventMessageFile - Provider={providerName}, SubKey={providerSubKey.Name}");

                continue;
            }

            var categoryMessageFilePath = providerSubKey.GetValue("CategoryMessageFile") as string;
            var parameterMessageFilePath = providerSubKey.GetValue("ParameterMessageFile") as string;

            _logger?.Debug(
                $"Found message file for legacy provider {providerName} in subkey {providerSubKey.Name}. EventMessageFile={eventMessageFilePath}, CategoryMessageFile={categoryMessageFilePath ?? "<null>"}, ParameterMessageFile={parameterMessageFilePath ?? "<null>"}.");

            // Filter by extension. The FltMgr provider puts a .sys file in the EventMessageFile value,
            // and trying to load that causes an access violation.
            var supportedExtensions = new[] { ".dll", ".exe" };

            var messageFiles = eventMessageFilePath
                .Split(';')
                .Where(path => supportedExtensions.Contains(Path.GetExtension(path).ToLower()))
                .ToList();

            IEnumerable<string> files;

            if (categoryMessageFilePath is not null)
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
            return files.Select(Environment.ExpandEnvironmentVariables).ToList();
        }

        _logger?.Debug($"No legacy EventMessageFile found for provider {providerName}");

        return [];
    }

    private class OpenEventLogRegistryKeyFailedException(string msg) : Exception(msg) {}
}
