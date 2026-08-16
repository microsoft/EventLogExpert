// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Logging.Abstractions;
using Microsoft.Win32;

namespace EventLogExpert.Eventing.ProviderMetadata;

internal sealed class RegistryProvider(ITraceLogger? logger = null) : ILegacyMessageFileResolver
{
    private readonly ITraceLogger? _logger = logger;

    // Local-only: mixing remote registry data with local modern metadata yields wrong message text.
    public IReadOnlyList<string> GetMessageFilesForLegacyProvider(string providerName)
    {
        _logger?.Debug($"{nameof(GetMessageFilesForLegacyProvider)} called for provider {providerName}");

        // Owned base key: Registry.LocalMachine is shared static state and must not be disposed per instance.
        using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default);

        const string EventLogKeyPath = @"SYSTEM\CurrentControlSet\Services\EventLog";

        using var eventLogKey = hklm.OpenSubKey(EventLogKeyPath) ??
            throw new OpenEventLogRegistryKeyFailedException(
                $@"Failed to open HKEY_LOCAL_MACHINE\{EventLogKeyPath}.");

        foreach (var logSubKeyName in eventLogKey.GetSubKeyNames())
        {
            if (LogChannelNames.RegistrySkipChannels.Contains(logSubKeyName))
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

            IReadOnlyList<string> messageFiles = LegacyMessageFilePaths.GetSupportedModulePaths(eventMessageFilePath);

            var orderedFiles = new List<string>(messageFiles.Count + 1);

            if (categoryMessageFilePath is not null) { orderedFiles.Add(categoryMessageFilePath); }

            orderedFiles.AddRange(messageFiles);

            // Expand first, then de-duplicate on the resolved path: the same module can appear as both the category and
            // an event-message-file entry, or via %SystemRoot% vs an absolute path, and loading it twice would feed
            // duplicate messages into disambiguation.
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<string>(orderedFiles.Count);

            for (int i = 0; i < orderedFiles.Count; i++)
            {
                string file = Environment.ExpandEnvironmentVariables(orderedFiles[i]);

                if (seen.Add(file)) { result.Add(file); }
            }

            return result;
        }

        _logger?.Debug($"No legacy EventMessageFile found for provider {providerName}");

        return [];
    }

    private class OpenEventLogRegistryKeyFailedException(string msg) : Exception(msg);
}
