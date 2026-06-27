// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.PublisherMetadata.Offline.Containment;
using EventLogExpert.Logging.Abstractions;
using Microsoft.Win32;

namespace EventLogExpert.Eventing.PublisherMetadata.Offline;

/// <summary>
///     One modern (manifest) publisher registered under the image's <c>WINEVT\Publishers</c>, with re-rooted file
///     paths.
/// </summary>
internal sealed record OfflinePublisherRegistration(
    Guid PublisherGuid,
    string ProviderName,
    string? ResourceFilePath,
    IReadOnlyList<string> MessageFilePaths,
    string? ParameterFilePath);

/// <summary>
///     Reads the modern publisher registrations from a foreign image's <c>SOFTWARE</c> hive (
///     <c>Microsoft\Windows\CurrentVersion\WINEVT\Publishers</c>). Each <c>{guid}</c> subkey carries the provider name as
///     its default value and the resource/message/parameter file paths as values, read without host environment expansion
///     and mapped onto the image. Malformed entries (non-GUID key, missing name) are skipped rather than failing the whole
///     read.
/// </summary>
internal sealed class OfflinePublisherCatalog(OfflineImagePathResolver pathResolver, ITraceLogger? logger)
{
    private const string PublishersKeyPath = @"Microsoft\Windows\CurrentVersion\WINEVT\Publishers";

    public IReadOnlyList<OfflinePublisherRegistration> ReadRegistrations(RegistryKey softwareRoot)
    {
        using RegistryKey? publishers = softwareRoot.OpenSubKey(PublishersKeyPath);

        if (publishers is null)
        {
            logger?.Debug($"{nameof(OfflinePublisherCatalog)}: {PublishersKeyPath} not present in the image SOFTWARE hive.");

            return [];
        }

        var registrations = new List<OfflinePublisherRegistration>();

        foreach (string subKeyName in publishers.GetSubKeyNames())
        {
            if (!Guid.TryParse(subKeyName, out Guid publisherGuid)) { continue; }

            using RegistryKey? publisherKey = publishers.OpenSubKey(subKeyName);

            if (publisherKey is null) { continue; }

            if (ReadString(publisherKey, name: null) is not { Length: > 0 } providerName) { continue; }

            registrations.Add(new OfflinePublisherRegistration(
                publisherGuid,
                providerName,
                pathResolver.Resolve(ReadString(publisherKey, "ResourceFileName"), "publisher resource"),
                pathResolver.ResolveMany(ReadString(publisherKey, "MessageFileName"), "publisher message file"),
                pathResolver.Resolve(ReadString(publisherKey, "ParameterFileName"), "publisher parameter file")));
        }

        return registrations;
    }

    // Read without host expansion: .NET otherwise expands REG_EXPAND_SZ against the HOST environment, so the literal
    // %token must reach the mapper. (No effect on REG_SZ values, so this is safe for both kinds.)
    private static string? ReadString(RegistryKey key, string? name) =>
        key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
}
