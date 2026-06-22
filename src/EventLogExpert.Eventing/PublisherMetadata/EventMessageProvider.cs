// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Interop;
using EventLogExpert.Eventing.Readers;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Provider.Resolution;
using System.Runtime.InteropServices;

namespace EventLogExpert.Eventing.PublisherMetadata;

/// <summary>
///     Represents an event provider on the local machine. EventLogExpert is a local-only tool; remote-machine
///     resolution is intentionally not supported.
/// </summary>
public sealed class EventMessageProvider(
    string providerName,
    IReadOnlyList<string>? metadataPaths = null,
    ITraceLogger? logger = null)
{
    private static readonly HashSet<string> s_allProviderNames = EventLogSession.GlobalSession.GetProviderNames();

    private readonly ITraceLogger? _logger = logger;
    private readonly IReadOnlyList<string>? _metadataPaths = metadataPaths;
    private readonly string _providerName = providerName;

    private RegistryProvider? _registryProvider;

    public ProviderDetails LoadProviderDetails() => LoadProviderDetailsCore(null);

    internal static string InjectMapAttribute(string template, string fieldName, string mapName)
    {
        string nameAttribute = $"name=\"{fieldName}\"";
        int searchStart = 0;

        while (true)
        {
            int dataIndex = template.IndexOf("<data", searchStart, StringComparison.OrdinalIgnoreCase);

            if (dataIndex < 0) { return template; }

            int afterTag = dataIndex + "<data".Length;
            char delimiter = afterTag < template.Length ? template[afterTag] : '\0';

            // "<data" prefixes "<dataSource"; only an element whose tag ends here is a real <data> node.
            if (delimiter is not (' ' or '\t' or '\r' or '\n' or '>' or '/'))
            {
                searchStart = afterTag;

                continue;
            }

            int elementEnd = template.IndexOf('>', afterTag);

            if (elementEnd < 0) { return template; }

            int nameIndex = template.IndexOf(nameAttribute, dataIndex, StringComparison.OrdinalIgnoreCase);

            if (nameIndex >= 0 && nameIndex < elementEnd)
            {
                return template.Insert(nameIndex + nameAttribute.Length, $" map=\"{mapName}\"");
            }

            searchStart = elementEnd + 1;
        }
    }

    internal static List<MessageModel> LoadMessagesFromFiles(
        IEnumerable<string> legacyProviderFiles,
        string providerName,
        ITraceLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(legacyProviderFiles);

        List<MessageModel> messages = [];

        foreach (var file in legacyProviderFiles)
        {
            if (!MessageTableReader.TryOpen(file, logger, out var handle, out nint memTable, out uint size)) { continue; }

            try { MessageTableReader.AppendMatches(memTable, size, providerName, -1, messages); }
            finally { handle.Dispose(); }
        }

        return messages;
    }

    private static void InjectMapAttributes(
        IReadOnlyList<EventModel> events,
        IReadOnlyDictionary<WevtEventKey, IReadOnlyDictionary<string, string>> eventFieldMaps,
        IReadOnlyDictionary<string, ValueMapDefinition> decodedMaps)
    {
        if (eventFieldMaps.Count == 0) { return; }

        foreach (EventModel model in events)
        {
            if (string.IsNullOrEmpty(model.Template)) { continue; }

            if (!eventFieldMaps.TryGetValue(
                    new WevtEventKey((uint)model.Id, model.Version),
                    out IReadOnlyDictionary<string, string>? fieldMaps))
            {
                continue;
            }

            string template = model.Template;

            foreach ((string fieldName, string mapName) in fieldMaps)
            {
                if (decodedMaps.ContainsKey(mapName))
                {
                    template = InjectMapAttribute(template, fieldName, mapName);
                }
            }

            model.Template = template;
        }
    }

    private LegacyMessageFileSource? BuildLazySource(IReadOnlyList<string> files)
    {
        if (files.Count == 0) { return null; }

        var walkable = new List<string>();
        int total = 0;

        foreach (var file in files)
        {
            if (!MessageTableReader.TryOpen(file, _logger, out var handle, out nint memTable, out uint size)) { continue; }

            try
            {
                int count = MessageTableReader.CountEntries(memTable, size);

                if (count > 0)
                {
                    walkable.Add(file);
                    total += count;
                }
            }
            finally { handle.Dispose(); }
        }

        return total > 0 ? new LegacyMessageFileSource(walkable, _providerName, total, _logger) : null;
    }

    private ProviderDetails LoadMessagesFromModernProvider(ProviderMetadata providerMetadata)
    {
        _logger?.Debug($"{nameof(LoadMessagesFromModernProvider)} called for provider {_providerName}");

        var provider = new ProviderDetails { ProviderName = _providerName };

        if (!providerMetadata.IsLocaleMetadata && !s_allProviderNames.Contains(_providerName))
        {
            _logger?.Debug($"{_providerName} modern provider is not present. Returning empty provider.");

            return provider;
        }

        try
        {
            provider.Events = providerMetadata.Events.Select(e => new EventModel
            {
                Description = e.Description,
                Id = e.Id,
                Keywords = e.Keywords.ToArray(),
                Level = e.Level,
                LogName = e.LogName,
                Opcode = e.Opcode,
                Task = e.Task,
                Template = e.Template,
                Version = e.Version
            }).ToArray();
        }
        catch (Exception ex)
        {
            _logger?.Debug($"Failed to load Events for modern provider: {_providerName}. Exception:\n{ex}");
        }

        try
        {
            provider.Keywords = providerMetadata.Keywords;
        }
        catch (Exception ex)
        {
            _logger?.Debug($"Failed to load Keywords for modern provider: {_providerName}. Exception:\n{ex}");
        }

        try
        {
            provider.Opcodes = providerMetadata.Opcodes;
        }
        catch (Exception ex)
        {
            _logger?.Debug($"Failed to load Opcodes for modern provider: {_providerName}. Exception:\n{ex}");
        }

        try
        {
            provider.Tasks = providerMetadata.Tasks;
        }
        catch (Exception ex)
        {
            _logger?.Debug($"Failed to load Tasks for modern provider: {_providerName}. Exception:\n{ex}");
        }

        PopulateValueMaps(provider, providerMetadata);

        _logger?.Debug($"Returning {provider.Events.Count} events for provider {_providerName}");

        return provider;
    }

    private ProviderDetails LoadProviderDetailsCore(HashSet<string>? visited)
    {
        var providerMetadata = ProviderMetadata.Create(_providerName, _metadataPaths, _logger);

        ProviderDetails provider = providerMetadata is not null
            ? LoadMessagesFromModernProvider(providerMetadata)
            : new ProviderDetails { ProviderName = _providerName };

        // When metadataPaths are provided, this is an MTA-only resolution path.
        // Skip registry and DLL lookups entirely.
        if (_metadataPaths is { Count: > 0 })
        {
            return provider;
        }

        _registryProvider ??= new RegistryProvider(_logger);

        var legacyProviderFiles = _registryProvider.GetMessageFilesForLegacyProvider(_providerName);

        if (!TrySetLazyMessages(provider, legacyProviderFiles))
        {
            if (!string.IsNullOrEmpty(providerMetadata?.MessageFilePath) &&
                TrySetLazyMessages(provider, [providerMetadata.MessageFilePath]))
            {
                _logger?.Debug(
                    $"No legacy messages loaded for provider {_providerName}. Using message file from modern provider.");
            }
            else
            {
                _logger?.Debug(
                    $"No message table loaded for provider {_providerName}. Returning empty provider details.");
            }
        }

        if (!string.IsNullOrEmpty(providerMetadata?.ParameterFilePath) &&
            !TrySetLazyParameters(provider, [providerMetadata.ParameterFilePath]))
        {
            _logger?.Debug($"Parameter file for provider {_providerName} produced no messages.");
        }

        if (provider.IsEmpty)
        {
            TryFallbackToOwningPublisher(provider, visited);
        }

        return provider;
    }

    private void PopulateValueMaps(ProviderDetails provider, ProviderMetadata providerMetadata)
    {
        try
        {
            Guid publisherGuid = providerMetadata.PublisherGuid;

            if (publisherGuid == Guid.Empty) { return; }

            string resourceFilePath = providerMetadata.ResourceFilePath;

            if (string.IsNullOrEmpty(resourceFilePath)) { return; }

            WevtTemplateData? templateData = WevtTemplateReader.TryRead(resourceFilePath, publisherGuid, _logger);

            if (templateData is null || templateData.Maps.Count == 0) { return; }

            Dictionary<string, ValueMapDefinition> decodedMaps = new(StringComparer.Ordinal);

            foreach ((string mapName, WevtRawMap rawMap) in templateData.Maps)
            {
                ValueMapDefinition? definition = ResolveMap(rawMap, providerMetadata);

                if (definition is not null)
                {
                    decodedMaps[mapName] = definition;
                }
            }

            if (decodedMaps.Count == 0) { return; }

            provider.Maps = decodedMaps;

            InjectMapAttributes(provider.Events, templateData.EventFieldMaps, decodedMaps);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException
                                       and not StackOverflowException
                                       and not AccessViolationException)
        {
            _logger?.Debug($"Failed to populate value maps for modern provider: {_providerName}. Exception:\n{ex}");
        }
    }

    private ValueMapDefinition? ResolveMap(WevtRawMap rawMap, ProviderMetadata providerMetadata)
    {
        List<ValueMapEntry> entries = new(rawMap.Entries.Count);

        foreach (WevtRawMapEntry entry in rawMap.Entries)
        {
            if (entry.MessageId == uint.MaxValue) { continue; }

            string name;

            try
            {
                name = providerMetadata.FormatMessageById(entry.MessageId);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException
                                           and not StackOverflowException
                                           and not AccessViolationException)
            {
                _logger?.Debug(
                    $"Failed to resolve map message {entry.MessageId} for provider {_providerName}: {ex.Message}");

                continue;
            }

            if (string.IsNullOrEmpty(name)) { continue; }

            entries.Add(new ValueMapEntry(entry.Value, name.TrimEnd('\0', '\r', '\n', '\t', ' ')));
        }

        return entries.Count > 0 ? new ValueMapDefinition(rawMap.IsBitMap, entries) : null;
    }

    private void TryFallbackToOwningPublisher(ProviderDetails target, HashSet<string>? visited)
    {
        // Bound the fallback in case channel/publisher misconfiguration creates a chain.
        const int MaxOwningPublisherHops = 4;

        visited ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (visited.Count >= MaxOwningPublisherHops)
        {
            _logger?.Debug(
                $"{nameof(TryFallbackToOwningPublisher)}: depth cap ({MaxOwningPublisherHops}) reached resolving {_providerName}; giving up.");

            return;
        }

        if (!visited.Add(_providerName))
        {
            _logger?.Debug(
                $"{nameof(TryFallbackToOwningPublisher)}: skipping - already attempted {_providerName} in this resolution chain.");

            return;
        }

        if (!TryGetChannelOwningPublisher(_providerName, out var owningPublisher) || owningPublisher is null)
        {
            return;
        }

        if (string.Equals(owningPublisher, _providerName, StringComparison.OrdinalIgnoreCase))
        {
            // Channel owns itself - nothing else to try.
            return;
        }

        _logger?.Debug(
            $"{nameof(TryFallbackToOwningPublisher)}: {_providerName} resolved to owning publisher {owningPublisher}; loading details from there.");

        var ownerDetails = new EventMessageProvider(owningPublisher, _metadataPaths, _logger)
            .LoadProviderDetailsCore(visited);

        if (ownerDetails.IsEmpty)
        {
            return;
        }

        target.Events = ownerDetails.Events;

        if (ownerDetails.MessageSource is not null) { target.SetLazyMessageSource(ownerDetails.MessageSource); }

        if (ownerDetails.ParameterSource is not null) { target.SetLazyParameterSource(ownerDetails.ParameterSource); }

        target.Keywords = ownerDetails.Keywords;
        target.Opcodes = ownerDetails.Opcodes;
        target.Tasks = ownerDetails.Tasks;
        target.Maps = ownerDetails.Maps;
        target.ResolvedFromOwningPublisher = owningPublisher;
    }

    private bool TryGetChannelOwningPublisher(string channelName, out string? publisher)
    {
        publisher = null;

        using var channelConfig = NativeMethods.EvtOpenChannelConfig(
            EventLogSession.GlobalSession.Handle,
            channelName,
            0);

        if (channelConfig.IsInvalid)
        {
            int openError = Marshal.GetLastWin32Error();

            _logger?.Debug(
                $"{nameof(TryGetChannelOwningPublisher)}: EvtOpenChannelConfig failed for {channelName}. Error: {openError} ({NativeMethods.FormatSystemMessage((uint)openError) ?? "unknown"})");

            return false;
        }

        IntPtr buffer = IntPtr.Zero;

        try
        {
            bool success = NativeMethods.EvtGetChannelConfigProperty(
                channelConfig,
                EvtChannelConfigPropertyId.EvtChannelConfigOwningPublisher,
                0,
                0,
                IntPtr.Zero,
                out int bufferSize);

            int sizeError = Marshal.GetLastWin32Error();

            if (!success && sizeError != Win32ErrorCodes.ERROR_INSUFFICIENT_BUFFER)
            {
                _logger?.Debug(
                    $"{nameof(TryGetChannelOwningPublisher)}: size probe failed for {channelName}. Error: {sizeError}");

                return false;
            }

            buffer = Marshal.AllocHGlobal(bufferSize);

            success = NativeMethods.EvtGetChannelConfigProperty(
                channelConfig,
                EvtChannelConfigPropertyId.EvtChannelConfigOwningPublisher,
                0,
                bufferSize,
                buffer,
                out _);

            if (!success)
            {
                int readError = Marshal.GetLastWin32Error();

                _logger?.Debug(
                    $"{nameof(TryGetChannelOwningPublisher)}: read failed for {channelName}. Error: {readError}");

                return false;
            }

            var variant = Marshal.PtrToStructure<EvtVariant>(buffer);
            var value = NativeMethods.ConvertVariant(variant) as string;

            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            publisher = value;
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException
                                       and not StackOverflowException
                                       and not AccessViolationException)
        {
            _logger?.Debug(
                $"{nameof(TryGetChannelOwningPublisher)}: unexpected failure for {channelName}.\n{ex}");

            return false;
        }
        finally
        {
            if (buffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }

    private bool TrySetLazyMessages(ProviderDetails provider, IReadOnlyList<string> files)
    {
        var source = BuildLazySource(files);

        if (source is null) { return false; }

        provider.SetLazyMessageSource(source);

        return true;
    }

    private bool TrySetLazyParameters(ProviderDetails provider, IReadOnlyList<string> files)
    {
        var source = BuildLazySource(files);

        if (source is null) { return false; }

        provider.SetLazyParameterSource(source);

        return true;
    }
}
