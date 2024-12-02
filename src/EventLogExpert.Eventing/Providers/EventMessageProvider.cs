// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Models;
using EventLogExpert.Eventing.Readers;
using System.Runtime.InteropServices;

namespace EventLogExpert.Eventing.Providers;

/// <summary>Represents an event provider from a particular machine.</summary>
public sealed class EventMessageProvider
{
    private static readonly HashSet<string> s_allProviderNames = EventLogSession.GlobalSession.GetProviderNames();

    private readonly ITraceLogger? _logger;
    private readonly string _providerName;
    private readonly RegistryProvider _registryProvider;

    public EventMessageProvider(string providerName, string? computerName, ITraceLogger? logger = null)
    {
        _providerName = providerName;
        _logger = logger;
        _registryProvider = new RegistryProvider(computerName, _logger);
    }

    public EventMessageProvider(string providerName, ITraceLogger? logger = null) : this(providerName, null, logger) { }

    public static List<MessageModel> GetMessages(
        IEnumerable<string> legacyProviderFiles,
        string providerName,
        ITraceLogger? logger = null)
    {
        List<MessageModel> messages = [];

        foreach (var file in legacyProviderFiles)
        {
            /*
             * https://stackoverflow.com/questions/33498244/marshaling-a-message-table-resource
             *
             * The approach documented there has some issues, so we deviate a bit.
             *
             * RT_MESSAGETABLE is not found unless LoadLibraryEx is called with LOAD_LIBRARY_AS_DATAFILE.
             * So we must use LoadLibraryEx below rather than LoadLibrary. Msedgeupdate.dll exposes this
             * issue.
             */

            // Splitting file path because this will not resolve %systemroot% and
            // will instead try to use drive:\windows\%systemroot%\system32\...
            using LibraryHandle hModule = NativeMethods.LoadLibraryExW(
                file.Split("\\").Last(),
                IntPtr.Zero,
                LoadLibraryFlags.LOAD_LIBRARY_AS_DATAFILE);

            using LibraryHandle msgTableInfo = NativeMethods.FindResourceExA(hModule, NativeMethods.RT_MESSAGETABLE, 1);
            int error = Marshal.GetLastWin32Error();

            if (msgTableInfo.IsInvalid)
            {
                logger?.Trace(
                    $"No message table found. Returning 0 messages from file:\n" +
                    $"{file}\n" +
                    $"Error: {error}");

                continue;
            }

            var msgTable = NativeMethods.LoadResource(hModule, msgTableInfo);
            var memTable = NativeMethods.LockResource(msgTable);

            var numberOfBlocks = Marshal.ReadInt32(memTable);
            var blockPtr = IntPtr.Add(memTable, 4);
            var blockSize = Marshal.SizeOf<MessageResourceBlock>();

            for (var i = 0; i < numberOfBlocks; i++)
            {
                var block = Marshal.PtrToStructure<MessageResourceBlock>(blockPtr);
                var entryPtr = IntPtr.Add(memTable, block.OffsetToEntries);

                for (var id = block.LowId; id <= block.HighId; id++)
                {
                    var length = Marshal.ReadInt16(entryPtr);
                    var flags = Marshal.ReadInt16(entryPtr, 2);
                    var textPtr = IntPtr.Add(entryPtr, 4);

                    string? text = flags switch
                    {
                        0 => Marshal.PtrToStringAnsi(textPtr),
                        1 => Marshal.PtrToStringUni(textPtr),
                        2 => Marshal.PtrToStringAnsi(textPtr),
                        // All the ESE messages are a single-byte character set
                        // but have flags of 2, which is not defined. So just
                        // treat it as ANSI I guess?
                        _ => "Error: Bad flags. Could not get text.",
                    };

                    // This is an event
                    messages.Add(new MessageModel
                    {
                        Text = text ?? string.Empty,
                        ShortId = (short)id,
                        ProviderName = providerName,
                        RawId = id
                    });

                    // Advance to the next id
                    entryPtr = IntPtr.Add(entryPtr, length);
                }

                // Advance to the next block
                blockPtr = IntPtr.Add(blockPtr, blockSize);
            }
        }

        return messages;
    }

    public ProviderDetails LoadProviderDetails()
    {
        var providerMetadata = ProviderMetadata.Create(_providerName, _logger);

        ProviderDetails provider = providerMetadata is not null
            ? LoadMessagesFromModernProvider(providerMetadata)
            : new ProviderDetails { ProviderName = _providerName };

        var legacyProviderFiles = _registryProvider.GetMessageFilesForLegacyProvider(_providerName);

        if (legacyProviderFiles.Any())
        {
            provider.Messages = LoadMessagesFromDlls(legacyProviderFiles);
        }
        else
        {
            if (string.IsNullOrEmpty(providerMetadata?.MessageFilePath))
            {
                _logger?.Trace($"No message files found for provider {_providerName}. Returning null.");
            }
            else
            {
                _logger?.Trace($"No message files found for provider {_providerName}. Using message file from modern provider.");

                provider.Messages = LoadMessagesFromDlls([providerMetadata.MessageFilePath]);
            }
        }

        if (!string.IsNullOrEmpty(providerMetadata?.ParameterFilePath))
        {
            provider.Parameters = LoadMessagesFromDlls([providerMetadata.ParameterFilePath]);
        }

        return provider;
    }

    /// <summary>
    ///     Loads the messages for a legacy provider from the files specified in
    ///     the registry. This information is stored at HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\EventLog
    /// </summary>
    /// <returns></returns>
    private List<MessageModel> LoadMessagesFromDlls(IEnumerable<string> messageFilePaths)
    {
        _logger?.Trace($"{nameof(LoadMessagesFromDlls)} called for files {string.Join(", ", messageFilePaths)}");

        try
        {
            var messages = GetMessages(messageFilePaths, _providerName, _logger);

            _logger?.Trace($"Returning {messages.Count} messages for provider {_providerName}");

            return messages;
        }
        catch (Exception ex)
        {
            // Hide the failure. We want to allow the results from the modern provider
            // to return even if we failed to load the legacy provider.
            _logger?.Trace($"Failed to load legacy provider data for {_providerName}.\n{ex}");
        }

        return [];
    }

    /// <summary>
    ///     Loads the messages for a modern provider. This info is stored at
    ///     Computer\HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\WINEVT
    /// </summary>
    private ProviderDetails LoadMessagesFromModernProvider(ProviderMetadata providerMetadata)
    {
        _logger?.Trace($"LoadMessagesFromModernProvider called for provider {_providerName}");

        var provider = new ProviderDetails { ProviderName = _providerName };

        if (!s_allProviderNames.Contains(_providerName))
        {
            _logger?.Trace($"{_providerName} modern provider is not present. Returning empty provider.");

            return provider;
        }

        try
        {
            provider.Events = providerMetadata.Events.Select(
                e => new EventModel
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
            _logger?.Trace($"Failed to load Events for modern provider: {_providerName}. Exception:\n{ex}");
        }

        try
        {
            provider.Keywords = providerMetadata.Keywords;

        }
        catch (Exception ex)
        {
            _logger?.Trace($"Failed to load Keywords for modern provider: {_providerName}. Exception:\n{ex}");
        }

        try
        {
            provider.Opcodes = providerMetadata.Opcodes;
        }
        catch (Exception ex)
        {
            _logger?.Trace($"Failed to load Opcodes for modern provider: {_providerName}. Exception:\n{ex}");
        }

        try
        {
            provider.Tasks = providerMetadata.Tasks;
        }
        catch (Exception ex)
        {
            _logger?.Trace($"Failed to load Tasks for modern provider: {_providerName}. Exception:\n{ex}");
        }

        _logger?.Trace($"Returning {provider.Events.Count()} events for provider {_providerName}");

        return provider;
    }
}
