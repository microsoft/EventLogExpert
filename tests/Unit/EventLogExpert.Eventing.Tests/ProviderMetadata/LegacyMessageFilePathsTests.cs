// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.ProviderMetadata;

namespace EventLogExpert.Eventing.Tests.ProviderMetadata;

public sealed class LegacyMessageFilePathsTests
{
    [Fact]
    public void GetSupportedModulePaths_DropsEmptyAndWhitespaceEntriesAndTrimsPaths()
    {
        var result = LegacyMessageFilePaths.GetSupportedModulePaths(
            @"C:\a.dll;;   ; C:\b.sys ");

        Assert.Equal([@"C:\a.dll", @"C:\b.sys"], result);
    }

    [Fact]
    public void GetSupportedModulePaths_EmptyValue_ReturnsEmpty()
    {
        Assert.Empty(LegacyMessageFilePaths.GetSupportedModulePaths(string.Empty));
    }

    [Fact]
    public void GetSupportedModulePaths_FiltersUnsupportedExtensions()
    {
        var result = LegacyMessageFilePaths.GetSupportedModulePaths(
            @"C:\keep.dll;C:\notes.txt;C:\keep.sys;C:\config.ini");

        Assert.Equal([@"C:\keep.dll", @"C:\keep.sys"], result);
    }

    [Fact]
    public void GetSupportedModulePaths_IncludesSysDriverModules()
    {
        // A driver's own .sys module holds the authoritative message table; it must not be filtered out the way the
        // pre-fix code did, or its events (e.g. mtkwecx 7001/1035) resolve against an unrelated system DLL instead.
        var result = LegacyMessageFilePaths.GetSupportedModulePaths(
            @"C:\Windows\System32\netevent.dll;C:\Windows\System32\drivers\example.sys");

        Assert.Equal(
            [@"C:\Windows\System32\netevent.dll", @"C:\Windows\System32\drivers\example.sys"],
            result);
    }

    [Fact]
    public void GetSupportedModulePaths_KeepsSupportedExtensionsInRegistryOrder()
    {
        var result = LegacyMessageFilePaths.GetSupportedModulePaths(
            @"C:\a.exe;C:\b.sys;C:\c.dll");

        Assert.Equal([@"C:\a.exe", @"C:\b.sys", @"C:\c.dll"], result);
    }

    [Fact]
    public void GetSupportedModulePaths_MatchesExtensionsCaseInsensitively()
    {
        var result = LegacyMessageFilePaths.GetSupportedModulePaths(
            @"C:\a.DLL;C:\b.SyS;C:\c.ExE");

        Assert.Equal([@"C:\a.DLL", @"C:\b.SyS", @"C:\c.ExE"], result);
    }
}
