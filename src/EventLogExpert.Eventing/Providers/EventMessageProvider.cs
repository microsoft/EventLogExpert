// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Models;
using EventLogExpert.Eventing.Readers;
using System.Runtime.InteropServices;

namespace EventLogExpert.Eventing.Providers;

/// <summary>
///     Represents an event provider on the local machine. EventLogExpert is a local-only tool;
///     remote-machine resolution is intentionally not supported.
/// </summary>
public sealed class EventMessageProvider(
    string providerName,
    IReadOnlyList<string>? metadataPaths = null,
    ITraceLogger? logger = null)
{
    private static readonly HashSet<string> s_allProviderNames = EventLogSession.GlobalSession.GetProviderNames();

    private readonly IReadOnlyList<string>? _metadataPaths = metadataPaths;
    private readonly ITraceLogger? _logger = logger;
    private readonly string _providerName = providerName;

    private RegistryProvider? _registryProvider;

    public static List<MessageModel> GetMessages(
        IEnumerable<string> legacyProviderFiles,
        string providerName,
        ITraceLogger? logger = null)
    {
        List<MessageModel> messages = [];

        foreach (var file in legacyProviderFiles)
        {
            using LibraryHandle hModule = LoadMessageModule(file, logger);

            if (hModule.IsInvalid)
            {
                continue;
            }

            // FindResourceEx returns an HRSRC that points into the already-loaded module's
            // resource section. It is owned by the module handle and must NOT be FreeLibrary'd.
            IntPtr msgTableInfo = NativeMethods.FindResourceExA(hModule, NativeMethods.RT_MESSAGETABLE, 1);
            int error = Marshal.GetLastWin32Error();

            if (msgTableInfo == IntPtr.Zero)
            {
                logger?.Debug(
                    $"No message table found. Returning 0 messages from file:\n{file}\nFindResourceEx error: {error} ({NativeMethods.FormatSystemMessage((uint)error) ?? "unknown"}). Error 1813 (ERROR_RESOURCE_TYPE_NOT_FOUND) commonly means the message table lives in a localized .mui satellite the loader could not locate, but it can also indicate a missing message table, a non-default resource id, or an unavailable language fallback.");

                continue;
            }

            var msgTable = NativeMethods.LoadResource(hModule, msgTableInfo);
            int loadResourceError = Marshal.GetLastWin32Error();

            if (msgTable == IntPtr.Zero)
            {
                logger?.Debug(
                    $"LoadResource returned NULL for message table in file:\n{file}\nError: {loadResourceError} ({NativeMethods.FormatSystemMessage((uint)loadResourceError) ?? "unknown"}).");

                continue;
            }

            var memTable = NativeMethods.LockResource(msgTable);

            if (memTable == IntPtr.Zero)
            {
                logger?.Debug($"LockResource returned NULL for message table in file:\n{file}");

                continue;
            }

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

    /// <summary>
    ///     Loads a message-resource module using flags that honor MUI satellite resolution, with
    ///     fallbacks for older binaries and unresolved paths. Returns an invalid handle on failure
    ///     (the caller is expected to skip).
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <c>LOAD_LIBRARY_AS_DATAFILE</c> alone does NOT trigger MUI satellite loading. Modern
    ///         Windows binaries (e.g., DriverStore-installed services, in-box system EXEs/DLLs)
    ///         keep their <c>RT_MESSAGETABLE</c> resources in <c>&lt;binary&gt;.mui</c> files under
    ///         language subfolders rather than in the binary itself. <c>FindResource</c> on a
    ///         module loaded with only <c>LOAD_LIBRARY_AS_DATAFILE</c> then returns 1813
    ///         (<c>ERROR_RESOURCE_TYPE_NOT_FOUND</c>). Combining
    ///         <c>LOAD_LIBRARY_AS_IMAGE_RESOURCE</c> with <c>LOAD_LIBRARY_AS_DATAFILE_EXCLUSIVE</c>
    ///         causes the loader to follow the MUI fallback chain — the same path
    ///         <c>EvtFormatMessage</c> (and Event Viewer MMC) uses.
    ///     </para>
    /// </remarks>
    private static LibraryHandle LoadMessageModule(string file, ITraceLogger? logger)
    {
        // Do NOT call Environment.ExpandEnvironmentVariables here. RegistryProvider already
        // expands legacy registry values, and ProviderMetadata.MessageFilePath is already a
        // fully resolved path. A second expansion here is unnecessary and could mask bugs in
        // upstream resolution.

        // Primary attempt: MUI-aware load using the path as given. Mirrors EvtFormatMessage behavior.
        const LoadLibraryFlags muiAwareFlags =
            LoadLibraryFlags.LOAD_LIBRARY_AS_IMAGE_RESOURCE |
            LoadLibraryFlags.LOAD_LIBRARY_AS_DATAFILE_EXCLUSIVE;

        var hModule = NativeMethods.LoadLibraryExW(file, IntPtr.Zero, muiAwareFlags);
        int error = Marshal.GetLastWin32Error();

        if (!hModule.IsInvalid)
        {
            logger?.Debug(
                $"LoadLibraryEx succeeded for {file} with flags LOAD_LIBRARY_AS_IMAGE_RESOURCE | LOAD_LIBRARY_AS_DATAFILE_EXCLUSIVE.");

            return hModule;
        }

        hModule.Dispose();

        var primaryFailureMessage =
            $"LoadLibraryEx failed for {file} with flags LOAD_LIBRARY_AS_IMAGE_RESOURCE | LOAD_LIBRARY_AS_DATAFILE_EXCLUSIVE. Error: {error} ({NativeMethods.FormatSystemMessage((uint)error) ?? "unknown"}).";

        // Legacy fallback: re-attempt the load using the leaf filename only, resolved under the
        // trusted system directory. Restrict this to inputs that are already pure leaf filenames
        // (no directory information of any kind). Both rooted inputs (e.g., "C:\foo.dll",
        // "C:foo.dll", "\Windows\foo.dll") and unrooted inputs that include a subdirectory
        // (e.g., "subdir\foo.dll") would have their directory portion stripped here and the bare
        // leaf name resolved against the default DLL search order, which could load a different
        // same-named binary and produce wrong message text. Path.IsPathRooted alone does NOT
        // catch the "subdir\foo.dll" case, so compare against Path.GetFileName instead.
        if (!string.Equals(file, Path.GetFileName(file), StringComparison.Ordinal))
        {
            logger?.Debug($"{primaryFailureMessage} Skipping leaf-name fallback because the input contains directory information.");

            return LibraryHandle.Zero;
        }

        var leafName = Path.GetFileName(file);

        if (string.IsNullOrEmpty(leafName))
        {
            logger?.Debug($"{primaryFailureMessage} Skipping leaf-name fallback because no leaf filename could be extracted.");

            return LibraryHandle.Zero;
        }

        // Constrain leaf-name resolution to the trusted system directory. Letting LoadLibraryEx
        // resolve a bare leaf name through the default DLL search order would search the
        // application directory first, which is a DLL planting / hijacking risk and could load
        // a same-named binary with bogus message text. Historically this fallback existed for
        // legacy registry values that named system binaries by leaf only (e.g., "wevtsvc.dll");
        // pinning resolution to %SystemRoot%\System32 preserves that behavior safely.
        var systemPath = Path.Combine(Environment.SystemDirectory, leafName);

        if (!File.Exists(systemPath))
        {
            logger?.Debug(
                $"{primaryFailureMessage} Skipping leaf-name fallback because '{leafName}' does not exist under {Environment.SystemDirectory}.");

            return LibraryHandle.Zero;
        }

        logger?.Debug(
            $"{primaryFailureMessage} Falling back to leaf-name resolution against the system directory: {systemPath}.");

        hModule = NativeMethods.LoadLibraryExW(systemPath, IntPtr.Zero, muiAwareFlags);

        error = Marshal.GetLastWin32Error();

        if (!hModule.IsInvalid)
        {
            logger?.Debug(
                $"LoadLibraryEx succeeded for {systemPath} (leaf-name fallback) with flags LOAD_LIBRARY_AS_IMAGE_RESOURCE | LOAD_LIBRARY_AS_DATAFILE_EXCLUSIVE.");

            return hModule;
        }

        hModule.Dispose();

        logger?.Debug(
            $"LoadLibraryEx failed for {systemPath} (leaf-name fallback) with flags LOAD_LIBRARY_AS_IMAGE_RESOURCE | LOAD_LIBRARY_AS_DATAFILE_EXCLUSIVE. Error: {error} ({NativeMethods.FormatSystemMessage((uint)error) ?? "unknown"}). Original requested file was: {file}.");

        return LibraryHandle.Zero;
    }

    public ProviderDetails LoadProviderDetails()
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

        if (legacyProviderFiles.Any())
        {
            provider.Messages = LoadMessagesFromDlls(legacyProviderFiles);
        }
        else
        {
            if (string.IsNullOrEmpty(providerMetadata?.MessageFilePath))
            {
                _logger?.Debug($"No message files found for provider {_providerName}. Returning null.");
            }
            else
            {
                _logger?.Debug(
                    $"No message files found for provider {_providerName}. Using message file from modern provider.");

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
    ///     Loads the messages for a legacy provider from the files specified in the registry. This information is stored
    ///     at HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\EventLog
    /// </summary>
    /// <returns></returns>
    private List<MessageModel> LoadMessagesFromDlls(IEnumerable<string> messageFilePaths)
    {
        _logger?.Debug($"{nameof(LoadMessagesFromDlls)} called for files {string.Join(", ", messageFilePaths)}");

        try
        {
            var messages = GetMessages(messageFilePaths, _providerName, _logger);

            _logger?.Debug($"Returning {messages.Count} messages for provider {_providerName}");

            return messages;
        }
        catch (Exception ex)
        {
            // Hide the failure. We want to allow the results from the modern provider
            // to return even if we failed to load the legacy provider.
            _logger?.Debug($"Failed to load legacy provider data for {_providerName}.\n{ex}");
        }

        return [];
    }

    /// <summary>
    ///     Loads the messages for a modern provider. This info is stored at
    ///     Computer\HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\WINEVT
    /// </summary>
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

        _logger?.Debug($"Returning {provider.Events.Count} events for provider {_providerName}");

        return provider;
    }
}
