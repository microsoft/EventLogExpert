// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Models;
using EventLogExpert.Eventing.Readers;
using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;

namespace EventLogExpert.Eventing.Providers;

/// <summary>
///     Represents an event provider from a particular machine.
/// </summary>
public class EventMessageProvider
{
    private static readonly HashSet<string> s_allProviderNames = EventLogSession.GlobalSession.GetProviderNames();

    private readonly string _providerName;
    private readonly RegistryProvider _registryProvider;
    private readonly Action<string, LogLevel> _traceAction;

    public EventMessageProvider(string providerName, Action<string, LogLevel> traceAction) :
        this(providerName, null, traceAction) { }

    public EventMessageProvider(string providerName, string? computerName, Action<string, LogLevel> traceAction)
    {
        _providerName = providerName;
        _traceAction = traceAction;
        _registryProvider = new RegistryProvider(computerName, _traceAction);
    }

    public static List<MessageModel> GetMessages(IEnumerable<string> legacyProviderFiles, string providerName, Action<string, LogLevel> _traceAction)
    {
        var messages = new List<MessageModel>();

        foreach (var file in legacyProviderFiles)
        {
            var hModule = nint.Zero;

            try
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

                // Splitting file path because this will not resolve %systemroot% and will instead try to use drive:\windows\%systemroot%\system32\...
                hModule = NativeMethods.LoadLibraryEx(file.Split("\\").Last(), nint.Zero, NativeMethods.LoadLibraryFlags.LOAD_LIBRARY_AS_DATAFILE);

                // TODO: Evaulate if there's any need to EnumResourceTypes.
                // This is an alternative approach to FindResource. Leaving it here until we're sure FindResource is good enough.
                // var result = NativeMethods.EnumResourceTypes(hModule, GetMessagesFromOneResource, IntPtr.Zero);

                var msgTableInfo =
                    NativeMethods.FindResource(hModule, 1, NativeMethods.RT_MESSAGETABLE);

                if (msgTableInfo == nint.Zero)
                {
                    _traceAction($"No message table found. Returning 0 messages from file: {file}", LogLevel.Information);
                    continue;
                }

                var msgTable = NativeMethods.LoadResource(hModule, msgTableInfo);
                var memTable = NativeMethods.LockResource(msgTable);

                var numberOfBlocks = Marshal.ReadInt32(memTable);
                var blockPtr = nint.Add(memTable, 4);
                var blockSize = Marshal.SizeOf<NativeMethods.MESSAGE_RESOURCE_BLOCK>();

                for (var i = 0; i < numberOfBlocks; i++)
                {
                    var block = Marshal.PtrToStructure<NativeMethods.MESSAGE_RESOURCE_BLOCK>(blockPtr);
                    var entryPtr = nint.Add(memTable, block.OffsetToEntries);

                    for (var id = block.LowId; id <= block.HighId; id++)
                    {
                        var length = Marshal.ReadInt16(entryPtr);
                        var flags = Marshal.ReadInt16(entryPtr, 2);
                        var textPtr = nint.Add(entryPtr, 4);
                        string? text;

                        if (flags == 0)
                        {
                            text = Marshal.PtrToStringAnsi(textPtr);
                        }
                        else if (flags == 1)
                        {
                            text = Marshal.PtrToStringUni(textPtr);
                        }
                        else if (flags == 2)
                        {
                            // All the ESE messages are a single-byte character set
                            // but have flags of 2, which is not defined. So just
                            // treat it as ANSI I guess?
                            text = Marshal.PtrToStringAnsi(textPtr);
                        }
                        else
                        {
                            text = "Error: Bad flags. Could not get text.";
                        }

                        // This is an event
                        messages.Add(new MessageModel
                        {
                            Text = text ?? string.Empty,
                            ShortId = (short)id,
                            ProviderName = providerName,
                            RawId = id
                        });

                        // Advance to the next id
                        entryPtr = nint.Add(entryPtr, length);
                    }

                    // Advance to the next block
                    blockPtr = nint.Add(blockPtr, blockSize);
                }
            }
            finally
            {
                if (hModule != nint.Zero)
                {
                    NativeMethods.FreeLibrary(hModule);
                }
            }
        }

        return messages;
    }

    public ProviderDetails? LoadProviderDetails()
    {
        ProviderMetadata? providerMetadata = null;

        try
        {
            providerMetadata = new ProviderMetadata(_providerName);
        }
        catch (Exception ex)
        {
            _traceAction($"Couldn't get metadata for provider {_providerName}. Exception: {ex}.", LogLevel.Information);
        }

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
                _traceAction($"No message files found for provider {_providerName}. Returning null.", LogLevel.Information);
            }
            else
            {
                _traceAction($"No message files found for provider {_providerName}. Using message file from modern provider.", LogLevel.Information);
                provider.Messages = LoadMessagesFromDlls([providerMetadata.MessageFilePath]);
            }
        }

        if (!string.IsNullOrEmpty(providerMetadata?.ParameterFilePath))
        {
            provider.Parameters = LoadMessagesFromDlls([providerMetadata.ParameterFilePath]);
        }

        if (provider.Events.Count <= 0 && provider.Messages.Count <= 0)
        {
            return null;
        }

        return provider;
    }

    private static bool GetMessagesFromOneResource(nint hModule, string lpszType, nint lParam)
    {
        // No need to implement this as long as we can use FindResource instead.
        return true;
    }

    /// <summary>
    ///     Loads the messages for a legacy provider from the files specified in
    ///     the registry. This information is stored at HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\EventLog
    /// </summary>
    /// <returns></returns>
    private List<MessageModel> LoadMessagesFromDlls(IEnumerable<string> messageFilePaths)
    {
        _traceAction($"{nameof(LoadMessagesFromDlls)} called for files {string.Join(", ", messageFilePaths)}", LogLevel.Information);

        try
        {
            var messages = GetMessages(messageFilePaths, _providerName, _traceAction);

            _traceAction($"Returning {messages.Count} messages for provider {_providerName}", LogLevel.Information);

            return messages;
        }
        catch (Exception ex)
        {
            // Hide the failure. We want to allow the results from the modern provider
            // to return even if we failed to load the legacy provider.
            _traceAction($"Failed to load legacy provider data for {_providerName}.", LogLevel.Information);
            _traceAction(ex.ToString(), LogLevel.Information);
        }

        return [];
    }

    /// <summary>
    ///     Loads the messages for a modern provider. This info is stored at
    ///     Computer\HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\WINEVT
    /// </summary>
    /// <returns></returns>
    private ProviderDetails LoadMessagesFromModernProvider(ProviderMetadata providerMetadata)
    {
        _traceAction($"LoadMessagesFromModernProvider called for provider {_providerName}", LogLevel.Information);

        var provider = new ProviderDetails { ProviderName = _providerName };

        if (!s_allProviderNames.Contains(_providerName))
        {
            _traceAction($"{_providerName} modern provider is not present. Returning empty provider.", LogLevel.Information);
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
                }).ToList();
        }
        catch (Exception ex)
        {
            _traceAction($"Failed to load Events for modern provider: {_providerName}. Exception:", LogLevel.Information);
            _traceAction(ex.ToString(), LogLevel.Information);
        }

        try
        {
            provider.Keywords = new Dictionary<long, string>(providerMetadata.Keywords);

        }
        catch (Exception ex)
        {
            _traceAction($"Failed to load Keywords for modern provider: {_providerName}. Exception:", LogLevel.Information);
            _traceAction(ex.ToString(), LogLevel.Information);
        }

        try
        {
            provider.Opcodes = new Dictionary<int, string>(providerMetadata.Opcodes);
        }
        catch (Exception ex)
        {
            _traceAction($"Failed to load Opcodes for modern provider: {_providerName}. Exception:", LogLevel.Information);
            _traceAction(ex.ToString(), LogLevel.Information);
        }

        try
        {
            provider.Tasks = new Dictionary<int, string>(providerMetadata.Tasks);
        }
        catch (Exception ex)
        {
            _traceAction($"Failed to load Tasks for modern provider: {_providerName}. Exception:", LogLevel.Information);
            _traceAction(ex.ToString(), LogLevel.Information);
        }

        _traceAction($"Returning {provider.Events?.Count} events for provider {_providerName}", LogLevel.Information);
        return provider;
    }
}
