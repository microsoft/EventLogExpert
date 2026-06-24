// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.PublisherMetadata;
using Microsoft.Win32;

namespace EventLogExpert.Eventing.IntegrationTests.PublisherMetadata;

public sealed class HostOsProvenanceTests
{
    [Fact]
    public void Empty_HasAllNullFields()
    {
        var empty = HostOsProvenance.Empty;

        Assert.Null(empty.Build);
        Assert.Null(empty.Revision);
        Assert.Null(empty.Edition);
        Assert.Null(empty.DisplayVersion);
    }

    [Fact]
    public void Read_OnWindowsHost_PopulatesEditionRevisionAndDisplayVersion()
    {
        using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default);
        using var currentVersion = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");

        Assert.NotNull(currentVersion);

        var expectedEdition = currentVersion.GetValue("EditionID") as string;
        var expectedDisplayVersion = currentVersion.GetValue("DisplayVersion") as string;

        // UBR is present on every supported build; the read must surface it as the recency secondary.
        var expectedRevision = currentVersion.GetValue("UBR") is int ubr ? ubr : (int?)null;

        var provenance = HostOsProvenance.Read();

        Assert.Equal(expectedEdition, provenance.Edition);
        Assert.Equal(expectedRevision, provenance.Revision);
        Assert.Equal(expectedDisplayVersion, provenance.DisplayVersion);
    }

    [Fact]
    public void Read_OnWindowsHost_ReturnsBuildMatchingRegistry()
    {
        // The host always has CurrentBuildNumber under CurrentVersion, so a live read must populate the recency
        // primary (Build). Compare against a direct registry read so the test is self-validating, not hard-coded.
        using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default);
        using var currentVersion = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");

        Assert.NotNull(currentVersion);

        var expectedBuild = int.TryParse(currentVersion.GetValue("CurrentBuildNumber") as string, out var build)
            ? build
            : (int?)null;

        var provenance = HostOsProvenance.Read();

        Assert.Equal(expectedBuild, provenance.Build);
        Assert.NotNull(provenance.Build);
    }
}
