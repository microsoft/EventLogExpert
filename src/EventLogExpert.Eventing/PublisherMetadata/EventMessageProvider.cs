// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Interop;
using EventLogExpert.Eventing.Readers;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Provider.Resolution;
using System.Diagnostics;
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

    private ProviderDetails LoadMessagesFromModernProvider(ProviderMetadata providerMetadata)
    {
        _logger?.Debug($"{nameof(LoadMessagesFromModernProvider)} called for provider {_providerName}");

        if (!providerMetadata.IsLocaleMetadata && !s_allProviderNames.Contains(_providerName))
        {
            _logger?.Debug($"{_providerName} modern provider is not present. Returning empty provider.");

            return new ProviderDetails { ProviderName = _providerName };
        }

        ProviderDetails provider =
            ProviderDetailsAssembler.Assemble(providerMetadata.ToRawContent(_providerName, _logger), _logger);

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

        var messageFilePaths = new List<string>();

        if (TrySetLazyMessages(provider, legacyProviderFiles))
        {
            messageFilePaths.AddRange(legacyProviderFiles);
        }
        else
        {
            var modernMessageFilePath = providerMetadata?.MessageFilePath;

            if (!string.IsNullOrEmpty(modernMessageFilePath) &&
                TrySetLazyMessages(provider, [modernMessageFilePath]))
            {
                messageFilePaths.Add(modernMessageFilePath);
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

        // Record the newest message-DLL file version as the per-provider recency signal (captured at db-create).
        // FileVersionInfo I/O runs once per provider here, never per event.
        SetMessageFileVersion(provider, messageFilePaths);

        if (provider.IsEmpty)
        {
            TryFallbackToOwningPublisher(provider, visited);
        }

        return provider;
    }

    // Stamps the provider with the newest 4-part numeric file version across its resolved message DLLs - the
    // per-provider recency signal. Uses FileVersionInfo NUMERIC parts, not the FileVersion string: inbox DLLs carry
    // a trailing " (WinBuild.160101.0800)" that Version.Parse rejects, which would null the ordinal for nearly every
    // OS provider. Leaves MessageFileVersion null when no candidate file yields a version.
    private void SetMessageFileVersion(ProviderDetails provider, IReadOnlyList<string> messageFilePaths)
    {
        Version? newest = null;

        foreach (var path in messageFilePaths)
        {
            var version = TryReadFileVersion(path);

            if (version is not null && (newest is null || version > newest)) { newest = version; }
        }

        if (newest is not null) { provider.MessageFileVersion = newest.ToString(); }
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

    private Version? TryReadFileVersion(string path)
    {
        try
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) { return null; }

            var info = FileVersionInfo.GetVersionInfo(path);

            return new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart, info.FilePrivatePart);
        }
        catch (Exception ex)
        {
            _logger?.Debug($"Failed to read file version for {path} (provider {_providerName}). Exception:\n{ex}");

            return null;
        }
    }

    private bool TrySetLazyMessages(ProviderDetails provider, IReadOnlyList<string> files)
    {
        var source = LegacyMessageFileSource.TryCreate(files, _providerName, _logger);

        if (source is null) { return false; }

        provider.SetLazyMessageSource(source);

        return true;
    }

    private bool TrySetLazyParameters(ProviderDetails provider, IReadOnlyList<string> files)
    {
        var source = LegacyMessageFileSource.TryCreate(files, _providerName, _logger);

        if (source is null) { return false; }

        provider.SetLazyParameterSource(source);

        return true;
    }
}
