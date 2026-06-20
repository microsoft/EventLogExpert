// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Update;

namespace EventLogExpert.Runtime.Tests.Update;

public sealed class UpdateServiceAssetSelectionTests
{
    private const string Arm64Name = "EventLogExpert_23.1.1.2_arm64.msix";
    private const string Arm64Uri =
        "https://github.com/microsoft/EventLogExpert/releases/download/v23.1.1.2/EventLogExpert_23.1.1.2_arm64.msix";

    private const string BundleName = "EventLogExpert_23.1.1.2.msixbundle";
    private const string BundleUri =
        "https://github.com/microsoft/EventLogExpert/releases/download/v23.1.1.2/EventLogExpert_23.1.1.2.msixbundle";

    private const string DotBundleName = "EventLogExpert.msixbundle";
    private const string DotBundleUri =
        "https://github.com/microsoft/EventLogExpert/releases/download/v23.1.1.2/EventLogExpert.msixbundle";

    private const string RuntimeName = "Microsoft.WindowsAppRuntime.1.7.msix";
    private const string RuntimeUri =
        "https://github.com/microsoft/EventLogExpert/releases/download/v23.1.1.2/Microsoft.WindowsAppRuntime.1.7.msix";

    private const string ToolsBundleName = "EventLogExpertTools_23.1.1.2.msixbundle";
    private const string ToolsBundleUri =
        "https://github.com/microsoft/EventLogExpert/releases/download/v23.1.1.2/EventLogExpertTools_23.1.1.2.msixbundle";

    private const string X64Name = "EventLogExpert_23.1.1.2_x64.msix";
    private const string X64Uri =
        "https://github.com/microsoft/EventLogExpert/releases/download/v23.1.1.2/EventLogExpert_23.1.1.2_x64.msix";

    [Fact]
    public void SelectUpdateDownloadUri_BundleAmidFullReleaseAssetSet_ReturnsBundleUri()
    {
        List<GitHubReleaseAsset> assets =
        [
            Asset(BundleName, BundleUri),
            Asset(X64Name, X64Uri),
            Asset(Arm64Name, Arm64Uri),
            Asset(RuntimeName, RuntimeUri)
        ];

        string? result = UpdateService.SelectUpdateDownloadUri(assets);

        Assert.Equal(BundleUri, result);
    }

    [Fact]
    public void SelectUpdateDownloadUri_BundleOnly_ReturnsBundleUri()
    {
        List<GitHubReleaseAsset> assets = [Asset(BundleName, BundleUri)];

        string? result = UpdateService.SelectUpdateDownloadUri(assets);

        Assert.Equal(BundleUri, result);
    }

    [Fact]
    public void SelectUpdateDownloadUri_DifferentlyCasedBundleName_MatchesCaseInsensitively()
    {
        List<GitHubReleaseAsset> assets = [Asset("eventlogexpert_23.1.1.2.MSIXBUNDLE", BundleUri)];

        string? result = UpdateService.SelectUpdateDownloadUri(assets);

        Assert.Equal(BundleUri, result);
    }

    [Fact]
    public void SelectUpdateDownloadUri_DotFormBundleName_ReturnsBundleUri()
    {
        List<GitHubReleaseAsset> assets =
        [
            Asset(DotBundleName, DotBundleUri),
            Asset(X64Name, X64Uri),
            Asset(Arm64Name, Arm64Uri)
        ];

        string? result = UpdateService.SelectUpdateDownloadUri(assets);

        Assert.Equal(DotBundleUri, result);
    }

    [Fact]
    public void SelectUpdateDownloadUri_EmptyAssets_ReturnsNull()
    {
        string? result = UpdateService.SelectUpdateDownloadUri([]);

        Assert.Null(result);
    }

    [Fact]
    public void SelectUpdateDownloadUri_MalformedNullNameAssetWithValidBundle_ReturnsBundleUriWithoutThrowing()
    {
        List<GitHubReleaseAsset> assets =
        [
            new GitHubReleaseAsset { Name = null!, Uri = "https://malformed" },
            Asset(BundleName, BundleUri)
        ];

        string? result = UpdateService.SelectUpdateDownloadUri(assets);

        Assert.Equal(BundleUri, result);
    }

    [Fact]
    public void SelectUpdateDownloadUri_NoBundleOnlyPerArchAndRuntime_ReturnsNull()
    {
        List<GitHubReleaseAsset> assets =
        [
            Asset(X64Name, X64Uri),
            Asset(Arm64Name, Arm64Uri),
            Asset(RuntimeName, RuntimeUri)
        ];

        string? result = UpdateService.SelectUpdateDownloadUri(assets);

        Assert.Null(result);
    }

    [Fact]
    public void SelectUpdateDownloadUri_NullAssets_ReturnsNull()
    {
        string? result = UpdateService.SelectUpdateDownloadUri(null);

        Assert.Null(result);
    }

    [Fact]
    public void SelectUpdateDownloadUri_RuntimeAssetOnly_ReturnsNull()
    {
        List<GitHubReleaseAsset> assets = [Asset(RuntimeName, RuntimeUri)];

        string? result = UpdateService.SelectUpdateDownloadUri(assets);

        Assert.Null(result);
    }

    [Fact]
    public void SelectUpdateDownloadUri_SiblingToolsBundleBeforeAppBundle_ReturnsAppBundleUri()
    {
        List<GitHubReleaseAsset> assets =
        [
            Asset(ToolsBundleName, ToolsBundleUri),
            Asset(BundleName, BundleUri)
        ];

        string? result = UpdateService.SelectUpdateDownloadUri(assets);

        Assert.Equal(BundleUri, result);
    }

    [Fact]
    public void SelectUpdateDownloadUri_SiblingToolsBundleOnly_ReturnsNull()
    {
        List<GitHubReleaseAsset> assets = [Asset(ToolsBundleName, ToolsBundleUri)];

        string? result = UpdateService.SelectUpdateDownloadUri(assets);

        Assert.Null(result);
    }

    [Fact]
    public void SelectUpdateDownloadUri_WhitespaceUriBundleBeforeValidBundle_ReturnsValidBundleUri()
    {
        List<GitHubReleaseAsset> assets =
        [
            Asset(BundleName, "   "),
            Asset(BundleName, BundleUri)
        ];

        string? result = UpdateService.SelectUpdateDownloadUri(assets);

        Assert.Equal(BundleUri, result);
    }

    [Fact]
    public void SelectUpdateDownloadUri_WhitespaceUriBundleOnly_ReturnsNull()
    {
        List<GitHubReleaseAsset> assets = [Asset(BundleName, "   ")];

        string? result = UpdateService.SelectUpdateDownloadUri(assets);

        Assert.Null(result);
    }

    private static GitHubReleaseAsset Asset(string name, string uri) => new() { Name = name, Uri = uri };
}
