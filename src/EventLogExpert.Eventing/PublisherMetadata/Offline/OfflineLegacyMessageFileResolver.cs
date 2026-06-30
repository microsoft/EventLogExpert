// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.PublisherMetadata.Offline.Containment;
using EventLogExpert.Logging.Abstractions;

namespace EventLogExpert.Eventing.PublisherMetadata.Offline;

internal sealed class OfflineLegacyMessageFileResolver(
    IOfflineRegistryKey systemRoot,
    OfflineImagePathResolver pathResolver,
    ITraceLogger? logger) : ILegacyMessageFileResolver
{
    private static readonly string[] s_supportedExtensions = [".dll", ".exe"];

    public IReadOnlyList<string> EnumerateProviderNames()
    {
        using IOfflineRegistryKey? eventLogKey = OpenEventLogKey();

        if (eventLogKey is null) { return []; }

        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string channelName in eventLogKey.GetSubKeyNames())
        {
            using IOfflineRegistryKey? channelKey = eventLogKey.OpenSubKey(channelName);

            if (channelKey is null) { continue; }

            foreach (string providerName in channelKey.GetSubKeyNames())
            {
                using IOfflineRegistryKey? providerKey = channelKey.OpenSubKey(providerName);

                if (providerKey is null) { continue; }

                if (ReadString(providerKey, "EventMessageFile") is { Length: > 0 } && seen.Add(providerName))
                {
                    names.Add(providerName);
                }
            }
        }

        return names;
    }

    public IReadOnlyList<string> GetMessageFilesForLegacyProvider(string providerName) =>
        ResolveFilesForLegacyProvider(providerName).MessageFiles;

    public IReadOnlyList<string> GetParameterFilesForLegacyProvider(string providerName) =>
        ResolveFilesForLegacyProvider(providerName).ParameterFiles;

    internal LegacyProviderFiles ResolveFilesForLegacyProvider(string providerName)
    {
        using IOfflineRegistryKey? eventLogKey = OpenEventLogKey();

        if (eventLogKey is null) { return LegacyProviderFiles.Empty; }

        foreach (string channelName in eventLogKey.GetSubKeyNames())
        {
            using IOfflineRegistryKey? channelKey = eventLogKey.OpenSubKey(channelName);
            using IOfflineRegistryKey? providerKey = channelKey?.OpenSubKey(providerName);

            if (providerKey is null) { continue; }

            // First channel with EventMessageFile wins to match native-built database parity.
            if (ReadString(providerKey, "EventMessageFile") is not { } eventMessageFile) { continue; }

            string? categoryMessageFile = ReadString(providerKey, "CategoryMessageFile");
            string? parameterMessageFile = ReadString(providerKey, "ParameterMessageFile");
            IReadOnlyList<string> parameterFiles = parameterMessageFile is null
                ? []
                : ResolveProviderFiles(parameterMessageFile, categoryMessageFile: null);
            IReadOnlyList<string> messageFiles = ResolveProviderFiles(eventMessageFile, categoryMessageFile);

            return new LegacyProviderFiles(messageFiles, parameterFiles);
        }

        return LegacyProviderFiles.Empty;
    }

    // Registry values stay unexpanded so image-local mapping never uses the host environment.
    private static string? ReadString(IOfflineRegistryKey key, string name) => key.GetValue(name) as string;

    private IOfflineRegistryKey? OpenEventLogKey()
    {
        if (systemRoot.OpenSubKey("Select") is not { } selectKey)
        {
            logger?.Debug($"{nameof(OfflineLegacyMessageFileResolver)}: SYSTEM\\Select not found in the image hive.");

            return null;
        }

        using (selectKey)
        {
            if (selectKey.GetValue("Current") is not int currentControlSet || currentControlSet <= 0)
            {
                logger?.Debug($"{nameof(OfflineLegacyMessageFileResolver)}: SYSTEM\\Select\\Current missing or invalid.");

                return null;
            }

            string eventLogPath = $@"ControlSet{currentControlSet:D3}\Services\EventLog";
            IOfflineRegistryKey? eventLogKey = systemRoot.OpenSubKey(eventLogPath);

            if (eventLogKey is null)
            {
                logger?.Debug($"{nameof(OfflineLegacyMessageFileResolver)}: {eventLogPath} not found in the image SYSTEM hive.");
            }

            return eventLogKey;
        }
    }

    private IReadOnlyList<string> ResolveProviderFiles(string eventMessageFile, string? categoryMessageFile)
    {
        // Filter raw registrations to .dll/.exe because some providers register .sys drivers that must not be loaded.
        var messageFiles = eventMessageFile
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(path => s_supportedExtensions.Contains(Path.GetExtension(path).ToLowerInvariant()))
            .ToList();

        var orderedRawFiles = new List<string>();

        // Use null, not empty, to mirror the live reader's category-first ordering.
        if (categoryMessageFile is not null)
        {
            orderedRawFiles.Add(categoryMessageFile);
            orderedRawFiles.AddRange(messageFiles.Where(path => !string.Equals(path, categoryMessageFile, StringComparison.Ordinal)));
        }
        else
        {
            orderedRawFiles.AddRange(messageFiles);
        }

        var resolved = new List<string>();

        foreach (string rawFile in orderedRawFiles)
        {
            if (pathResolver.Resolve(rawFile, "legacy message file") is { } file) { resolved.Add(file); }
        }

        return resolved;
    }

    internal sealed record LegacyProviderFiles(
        IReadOnlyList<string> MessageFiles,
        IReadOnlyList<string> ParameterFiles)
    {
        public static LegacyProviderFiles Empty { get; } = new([], []);
    }
}
