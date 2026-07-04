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
            if (LogChannelNames.AdminOnlyLiveChannels.Contains(logSubKeyName))
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

            // Loading FltMgr's .sys EventMessageFile causes an access violation; only datafile DLL/EXE loads are safe.
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

            return files.Select(Environment.ExpandEnvironmentVariables).ToList();
        }

        _logger?.Debug($"No legacy EventMessageFile found for provider {providerName}");

        return [];
    }

    private class OpenEventLogRegistryKeyFailedException(string msg) : Exception(msg)
    {
    }
}
