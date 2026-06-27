// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.PublisherMetadata.Offline.Containment;
using EventLogExpert.Logging.Abstractions;
using Microsoft.Win32;

namespace EventLogExpert.Eventing.PublisherMetadata.Offline;

/// <summary>
///     Offline counterpart of <see cref="RegistryProvider" />: resolves a legacy provider's message/category files
///     from a foreign image's <c>SYSTEM</c> hive instead of the host <c>HKLM\SYSTEM</c>. A statically loaded hive has no
///     <c>CurrentControlSet</c> symlink, so the active control set is resolved via <c>Select\Current</c> -&gt;
///     <c>ControlSet{NNN}</c>. The extension filter, category-first ordering, and the <c>ParameterMessageFile</c> discard
///     mirror <see cref="RegistryProvider" /> for parity with native-built databases; values are read without host
///     environment expansion and re-rooted onto the image. Unlike the live reader it does NOT skip the Security/State
///     admin channels - reading a hive file needs no elevation, so offline is intentionally more complete there.
/// </summary>
internal sealed class OfflineLegacyMessageFileResolver(
    RegistryKey systemRoot,
    OfflineImagePathResolver pathResolver,
    ITraceLogger? logger) : ILegacyMessageFileResolver
{
    private static readonly string[] s_supportedExtensions = [".dll", ".exe"];

    /// <summary>
    ///     Distinct legacy provider names registered under any channel that carry an <c>EventMessageFile</c>. Used to
    ///     discover legacy providers in the image.
    /// </summary>
    public IReadOnlyList<string> EnumerateProviderNames()
    {
        using RegistryKey? eventLogKey = OpenEventLogKey();

        if (eventLogKey is null) { return []; }

        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string channelName in eventLogKey.GetSubKeyNames())
        {
            using RegistryKey? channelKey = eventLogKey.OpenSubKey(channelName);

            if (channelKey is null) { continue; }

            foreach (string providerName in channelKey.GetSubKeyNames())
            {
                using RegistryKey? providerKey = channelKey.OpenSubKey(providerName);

                if (providerKey is null) { continue; }

                if (ReadString(providerKey, "EventMessageFile") is { Length: > 0 } && seen.Add(providerName))
                {
                    names.Add(providerName);
                }
            }
        }

        return names;
    }

    public IReadOnlyList<string> GetMessageFilesForLegacyProvider(string providerName)
    {
        using RegistryKey? eventLogKey = OpenEventLogKey();

        if (eventLogKey is null) { return []; }

        foreach (string channelName in eventLogKey.GetSubKeyNames())
        {
            using RegistryKey? channelKey = eventLogKey.OpenSubKey(channelName);
            using RegistryKey? providerKey = channelKey?.OpenSubKey(providerName);

            if (providerKey is null) { continue; }

            // Mirror RegistryProvider exactly (for parity with native-built databases): the first channel carrying an
            // EventMessageFile wins, even if extension filtering later empties it. ParameterMessageFile is deliberately
            // not read - the live reader reads but discards it.
            if (ReadString(providerKey, "EventMessageFile") is not { } eventMessageFile) { continue; }

            return ResolveProviderFiles(eventMessageFile, ReadString(providerKey, "CategoryMessageFile"));
        }

        return [];
    }

    // Read without host expansion so REG_EXPAND_SZ tokens reach the mapper as literal %tokens (no effect on REG_SZ).
    private static string? ReadString(RegistryKey key, string name) =>
        key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;

    private RegistryKey? OpenEventLogKey()
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
            RegistryKey? eventLogKey = systemRoot.OpenSubKey(eventLogPath);

            if (eventLogKey is null)
            {
                logger?.Debug($"{nameof(OfflineLegacyMessageFileResolver)}: {eventLogPath} not found in the image SYSTEM hive.");
            }

            return eventLogKey;
        }
    }

    private IReadOnlyList<string> ResolveProviderFiles(string eventMessageFile, string? categoryMessageFile)
    {
        // Filter to .dll/.exe on the raw value, mirroring the live reader (FltMgr registers a .sys driver here that
        // must not be loaded).
        var messageFiles = eventMessageFile
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(path => s_supportedExtensions.Contains(Path.GetExtension(path).ToLowerInvariant()))
            .ToList();

        var orderedRawFiles = new List<string>();

        // `is not null` (not IsNullOrEmpty) mirrors the live reader's category-first ordering exactly.
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
}
