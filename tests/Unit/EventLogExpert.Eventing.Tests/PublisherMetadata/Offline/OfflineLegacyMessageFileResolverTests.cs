// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.PublisherMetadata.Offline;
using EventLogExpert.Eventing.PublisherMetadata.Offline.Containment;
using Microsoft.Win32;

namespace EventLogExpert.Eventing.Tests.PublisherMetadata.Offline;

public sealed class OfflineLegacyMessageFileResolverTests
{
    [Fact]
    public void EnumerateProviderNames_ReturnsProvidersAcrossChannels()
    {
        using OfflineTestImage image = OfflineTestImage.Create(seedSystem: system =>
        {
            SetCurrentControlSet(system, 1);
            using (RegistryKey app = system.CreateSubKey(@"ControlSet001\Services\EventLog\Application\AppProvider"))
            {
                app.SetValue("EventMessageFile", @"C:\Windows\System32\app.dll", RegistryValueKind.ExpandString);
            }

            using RegistryKey security = system.CreateSubKey(@"ControlSet001\Services\EventLog\Security\SecProvider");
            security.SetValue("EventMessageFile", @"C:\Windows\System32\sec.dll", RegistryValueKind.ExpandString);
        });
        using OfflineRegistryHive hive = LoadSystemHive(image);

        IReadOnlyList<string> names = ResolverFor(image, hive).EnumerateProviderNames();

        Assert.Contains("AppProvider", names);
        Assert.Contains("SecProvider", names);
    }

    [Fact]
    public void GetMessageFiles_FiltersNonDllExeExtensions()
    {
        using OfflineTestImage image = OfflineTestImage.Create(seedSystem: system =>
        {
            SetCurrentControlSet(system, 1);
            using RegistryKey provider = system.CreateSubKey(@"ControlSet001\Services\EventLog\Application\DriverProvider");
            provider.SetValue("EventMessageFile", @"C:\Windows\System32\drivers\flt.sys;C:\Windows\System32\evt.dll", RegistryValueKind.ExpandString);
        });
        using OfflineRegistryHive hive = LoadSystemHive(image);

        IReadOnlyList<string> files = ResolverFor(image, hive).GetMessageFilesForLegacyProvider("DriverProvider");

        Assert.Equal(Path.Combine(image.RootDirectory, "Windows", "System32", "evt.dll"), Assert.Single(files), ignoreCase: true);
    }

    [Fact]
    public void GetMessageFiles_HonorsSelectCurrentControlSet()
    {
        using OfflineTestImage image = OfflineTestImage.Create(seedSystem: system =>
        {
            SetCurrentControlSet(system, 2);
            using RegistryKey provider = system.CreateSubKey(@"ControlSet002\Services\EventLog\Application\OnSetTwo");
            provider.SetValue("EventMessageFile", @"C:\Windows\System32\two.dll", RegistryValueKind.ExpandString);
        });
        using OfflineRegistryHive hive = LoadSystemHive(image);

        IReadOnlyList<string> files = ResolverFor(image, hive).GetMessageFilesForLegacyProvider("OnSetTwo");

        Assert.Equal(Path.Combine(image.RootDirectory, "Windows", "System32", "two.dll"), Assert.Single(files), ignoreCase: true);
    }

    [Fact]
    public void GetMessageFiles_OrdersCategoryFirstAndDiscardsParameterFile()
    {
        using OfflineTestImage image = OfflineTestImage.Create(seedSystem: SeedApplicationProvider);
        using OfflineRegistryHive hive = LoadSystemHive(image);

        IReadOnlyList<string> files = ResolverFor(image, hive).GetMessageFilesForLegacyProvider("TestLegacyProvider");

        Assert.Equal(2, files.Count);
        Assert.Equal(Path.Combine(image.RootDirectory, "Windows", "System32", "cat.dll"), files[0], ignoreCase: true);
        Assert.Equal(Path.Combine(image.RootDirectory, "Windows", "System32", "evt.dll"), files[1], ignoreCase: true);

        // The parameter message file is read by the live reader but never returned; the offline reader mirrors the
        // discard so offline databases stay comparable with native-built ones.
        Assert.DoesNotContain(files, file => file.EndsWith("param.dll", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GetMessageFiles_ReadsAdminChannelsThatTheLiveReaderSkips()
    {
        using OfflineTestImage image = OfflineTestImage.Create(seedSystem: system =>
        {
            SetCurrentControlSet(system, 1);
            using RegistryKey provider = system.CreateSubKey(@"ControlSet001\Services\EventLog\Security\SecAuditProvider");
            provider.SetValue("EventMessageFile", @"C:\Windows\System32\sec.dll", RegistryValueKind.ExpandString);
        });
        using OfflineRegistryHive hive = LoadSystemHive(image);

        IReadOnlyList<string> files = ResolverFor(image, hive).GetMessageFilesForLegacyProvider("SecAuditProvider");

        Assert.Equal(Path.Combine(image.RootDirectory, "Windows", "System32", "sec.dll"), Assert.Single(files), ignoreCase: true);
    }

    [Fact]
    public void GetMessageFiles_UnknownProvider_ReturnsEmpty()
    {
        using OfflineTestImage image = OfflineTestImage.Create(seedSystem: SeedApplicationProvider);
        using OfflineRegistryHive hive = LoadSystemHive(image);

        Assert.Empty(ResolverFor(image, hive).GetMessageFilesForLegacyProvider("NoSuchProvider"));
    }

    private static OfflineRegistryHive LoadSystemHive(OfflineTestImage image)
    {
        OfflineRegistryHive? hive = OfflineRegistryHive.TryLoad(image.ImageRoot.SystemHivePath, logger: null).Hive;
        Assert.NotNull(hive);

        return hive!;
    }

    private static OfflineLegacyMessageFileResolver ResolverFor(OfflineTestImage image, OfflineRegistryHive hive)
    {
        var pathResolver = new OfflineImagePathResolver(
            new OfflineImagePathMapper(image.ImageRoot, logger: null),
            new OfflineRootGuard(image.ImageRoot, logger: null));

        return new OfflineLegacyMessageFileResolver(hive.Root, pathResolver, logger: null);
    }

    private static void SeedApplicationProvider(RegistryKey system)
    {
        SetCurrentControlSet(system, 1);
        using RegistryKey provider = system.CreateSubKey(@"ControlSet001\Services\EventLog\Application\TestLegacyProvider");
        provider.SetValue("EventMessageFile", @"%SystemRoot%\System32\evt.dll", RegistryValueKind.ExpandString);
        provider.SetValue("CategoryMessageFile", @"C:\Windows\System32\cat.dll", RegistryValueKind.ExpandString);
        provider.SetValue("ParameterMessageFile", @"C:\Windows\System32\param.dll", RegistryValueKind.ExpandString);
    }

    private static void SetCurrentControlSet(RegistryKey system, int current)
    {
        using RegistryKey select = system.CreateSubKey("Select");
        select.SetValue("Current", current, RegistryValueKind.DWord);
    }
}
