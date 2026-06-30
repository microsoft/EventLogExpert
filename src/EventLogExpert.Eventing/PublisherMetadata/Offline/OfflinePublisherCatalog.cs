// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.PublisherMetadata.Offline.Containment;
using EventLogExpert.Logging.Abstractions;

namespace EventLogExpert.Eventing.PublisherMetadata.Offline;

internal sealed record OfflinePublisherRegistration(
    Guid PublisherGuid,
    string ProviderName,
    string? ResourceFilePath,
    IReadOnlyList<string> MessageFilePaths,
    string? ParameterFilePath);

internal sealed class OfflinePublisherCatalog(OfflineImagePathResolver pathResolver, ITraceLogger? logger)
{
    private const string PublishersKeyPath = @"Microsoft\Windows\CurrentVersion\WINEVT\Publishers";

    public IReadOnlyList<OfflinePublisherRegistration> ReadRegistrations(IOfflineRegistryKey softwareRoot)
    {
        using IOfflineRegistryKey? publishers = softwareRoot.OpenSubKey(PublishersKeyPath);

        if (publishers is null)
        {
            logger?.Debug($"{nameof(OfflinePublisherCatalog)}: {PublishersKeyPath} not present in the image SOFTWARE hive.");

            return [];
        }

        var registrations = new List<OfflinePublisherRegistration>();

        foreach (string subKeyName in publishers.GetSubKeyNames())
        {
            if (!Guid.TryParse(subKeyName, out Guid publisherGuid)) { continue; }

            using IOfflineRegistryKey? publisherKey = publishers.OpenSubKey(subKeyName);

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

    // Registry values stay unexpanded so image-local mapping never uses the host environment.
    private static string? ReadString(IOfflineRegistryKey key, string? name) => key.GetValue(name) as string;
}
