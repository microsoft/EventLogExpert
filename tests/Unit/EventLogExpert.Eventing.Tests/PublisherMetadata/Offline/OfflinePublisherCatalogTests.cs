// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.PublisherMetadata.Offline;
using EventLogExpert.Eventing.PublisherMetadata.Offline.Containment;
using Microsoft.Win32;

namespace EventLogExpert.Eventing.Tests.PublisherMetadata.Offline;

public sealed class OfflinePublisherCatalogTests
{
    private const string PublishersKeyPath = @"Microsoft\Windows\CurrentVersion\WINEVT\Publishers";
    private const string TestGuid = "{11111111-1111-1111-1111-111111111111}";

    [Fact]
    public void ReadRegistrations_MultiValueMessageFile_IsSplitAndReRooted()
    {
        using OfflineTestImage image = OfflineTestImage.Create(seedSoftware: software =>
        {
            using RegistryKey publisher = software.CreateSubKey($@"{PublishersKeyPath}\{TestGuid}");
            publisher.SetValue(null, "Test-Provider");
            publisher.SetValue("MessageFileName", @"C:\Windows\System32\a.dll;C:\Windows\System32\b.dll", RegistryValueKind.ExpandString);
        });

        OfflinePublisherRegistration registration = Assert.Single(ReadRegistrations(image));

        Assert.Equal(2, registration.MessageFilePaths.Count);
        Assert.Equal(Path.Combine(image.RootDirectory, "Windows", "System32", "a.dll"), registration.MessageFilePaths[0], ignoreCase: true);
        Assert.Equal(Path.Combine(image.RootDirectory, "Windows", "System32", "b.dll"), registration.MessageFilePaths[1], ignoreCase: true);
    }

    [Fact]
    public void ReadRegistrations_ReadsNameAndReRootsPaths()
    {
        using OfflineTestImage image = OfflineTestImage.Create(seedSoftware: software =>
        {
            using RegistryKey publisher = software.CreateSubKey($@"{PublishersKeyPath}\{TestGuid}");
            publisher.SetValue(null, "Test-Provider");
            publisher.SetValue("ResourceFileName", @"%SystemRoot%\System32\test.dll", RegistryValueKind.ExpandString);
            publisher.SetValue("MessageFileName", @"C:\Windows\System32\msg.dll", RegistryValueKind.ExpandString);
            publisher.SetValue("ParameterFileName", @"C:\Windows\System32\param.dll", RegistryValueKind.ExpandString);
        });

        IReadOnlyList<OfflinePublisherRegistration> registrations = ReadRegistrations(image);

        OfflinePublisherRegistration registration = Assert.Single(registrations);
        Assert.Equal(Guid.Parse(TestGuid), registration.PublisherGuid);
        Assert.Equal("Test-Provider", registration.ProviderName);
        Assert.Equal(Path.Combine(image.RootDirectory, "Windows", "System32", "test.dll"), registration.ResourceFilePath, ignoreCase: true);
        Assert.Equal(Path.Combine(image.RootDirectory, "Windows", "System32", "msg.dll"), Assert.Single(registration.MessageFilePaths), ignoreCase: true);
        Assert.Equal(Path.Combine(image.RootDirectory, "Windows", "System32", "param.dll"), registration.ParameterFilePath, ignoreCase: true);
    }

    [Fact]
    public void ReadRegistrations_RegExpandSzValue_IsReadLiterallyNotHostExpanded()
    {
        // If the catalog let .NET host-expand the REG_EXPAND_SZ value, %APPDATA% would become a real host path (e.g.
        // C:\Users\<user>\AppData\Roaming\…) that the mapper re-roots to a non-null image path. Reading it literally
        // (DoNotExpandEnvironmentNames) leaves the per-user token intact, and the mapper does not map per-user tokens,
        // so it drops the value - which is what proves the host environment is never consulted.
        using OfflineTestImage image = OfflineTestImage.Create(seedSoftware: software =>
        {
            using RegistryKey publisher = software.CreateSubKey($@"{PublishersKeyPath}\{TestGuid}");
            publisher.SetValue(null, "Test-Provider");
            publisher.SetValue("ResourceFileName", @"%APPDATA%\Vendor\foo.dll", RegistryValueKind.ExpandString);
        });

        OfflinePublisherRegistration registration = Assert.Single(ReadRegistrations(image));

        Assert.Null(registration.ResourceFilePath);
    }

    [Fact]
    public void ReadRegistrations_SkipsMalformedGuidAndNamelessPublishers()
    {
        using OfflineTestImage image = OfflineTestImage.Create(seedSoftware: software =>
        {
            using (RegistryKey notAGuid = software.CreateSubKey($@"{PublishersKeyPath}\not-a-guid"))
            {
                notAGuid.SetValue(null, "Ignored");
            }

            using (RegistryKey nameless = software.CreateSubKey($@"{PublishersKeyPath}\{{22222222-2222-2222-2222-222222222222}}"))
            {
                nameless.SetValue("ResourceFileName", @"C:\Windows\System32\x.dll", RegistryValueKind.ExpandString);
            }

            using RegistryKey valid = software.CreateSubKey($@"{PublishersKeyPath}\{TestGuid}");
            valid.SetValue(null, "Test-Provider");
        });

        OfflinePublisherRegistration registration = Assert.Single(ReadRegistrations(image));

        Assert.Equal("Test-Provider", registration.ProviderName);
    }

    [Fact]
    public void ReadRegistrations_WhenPublishersKeyAbsent_ReturnsEmpty()
    {
        using OfflineTestImage image = OfflineTestImage.Create(seedSoftware: software =>
            software.CreateSubKey(@"Microsoft\Windows\CurrentVersion").Dispose());

        Assert.Empty(ReadRegistrations(image));
    }

    private static IReadOnlyList<OfflinePublisherRegistration> ReadRegistrations(OfflineTestImage image)
    {
        var pathResolver = new OfflineImagePathResolver(
            new OfflineImagePathMapper(image.ImageRoot, logger: null),
            new OfflineRootGuard(image.ImageRoot, logger: null));
        var catalog = new OfflinePublisherCatalog(pathResolver, logger: null);

        using OfflineHiveFile? hive = OfflineHiveFile.TryOpen(image.ImageRoot.SoftwareHivePath, logger: null);
        Assert.NotNull(hive);

        return catalog.ReadRegistrations(hive!);
    }
}
