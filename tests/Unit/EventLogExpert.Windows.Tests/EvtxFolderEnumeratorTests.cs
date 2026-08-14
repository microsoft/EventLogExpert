// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Windows.Tests.TestUtils;
using EventLogExpert.Windows.Tests.TestUtils.Constants;
using EventLogExpert.WindowsPlatform.Activation;
using Xunit;

namespace EventLogExpert.Windows.Tests;

public sealed class EvtxFolderEnumeratorTests : IDisposable
{
    private readonly string _tempRoot = EvtxFolderFixtures.CreateTempTestFolder();

    public void Dispose()
    {
        EvtxFolderFixtures.TryDeleteFolder(_tempRoot);
    }

    [Fact]
    public void EnumerateEvtx_DoesNotRecurseIntoSubfolders()
    {
        var sub = Path.Combine(_tempRoot, "sub");
        Directory.CreateDirectory(sub);
        EvtxFolderFixtures.WriteEmptyFile(sub, "nested.evtx");
        EvtxFolderFixtures.WriteEmptyFile(_tempRoot, "top.evtx");

        var result = EvtxFolderEnumerator.EnumerateEvtx(_tempRoot, includeSubfolders: false, TestContext.Current.CancellationToken);

        var success = Assert.IsType<EvtxEnumerationResult.Success>(result);
        Assert.Single(success.Files);
        Assert.EndsWith("top.evtx", success.Files[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EnumerateEvtx_OnEmptyFolder_ReturnsEmptyVariant()
    {
        var result = EvtxFolderEnumerator.EnumerateEvtx(_tempRoot, includeSubfolders: false, TestContext.Current.CancellationToken);

        Assert.IsType<EvtxEnumerationResult.Empty>(result);
    }

    [Fact]
    public void EnumerateEvtx_OnFolderWithEvtxAndOtherFiles_ReturnsOnlyEvtx()
    {
        EvtxFolderFixtures.WriteEmptyFile(_tempRoot, "a.evtx");
        EvtxFolderFixtures.WriteEmptyFile(_tempRoot, "b.evtx");
        EvtxFolderFixtures.WriteEmptyFile(_tempRoot, "ignored.txt");
        EvtxFolderFixtures.WriteEmptyFile(_tempRoot, "ignored.log");

        var result = EvtxFolderEnumerator.EnumerateEvtx(_tempRoot, includeSubfolders: false, TestContext.Current.CancellationToken);

        var success = Assert.IsType<EvtxEnumerationResult.Success>(result);
        Assert.Equal(2, success.Files.Count);
        Assert.All(success.Files, f => Assert.EndsWith(Constants.EvtxExtension, f, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EnumerateEvtx_OnLongPathBeyondMax_StillReturnsEvtxFiles()
    {
        // Uses the \\?\ prefix to bypass the test process's MAX_PATH limit during setup; the
        // prefix is the cross-process portable long-path mechanism on Windows. The packaged
        // MAUI head exe is independently long-path-aware via its embedded app.manifest, so a
        // non-prefixed >MAX_PATH path also works when the OS LongPathsEnabled policy is set.
        // This test pins one specific claim: EvtxFolderEnumerator does not impose its own
        // artificial path-length cap (it forwards to Directory.EnumerateFiles which honors
        // the prefix-based long-path API regardless of process awareness).
        var nested = Path.Combine(_tempRoot, new string('a', 80), new string('b', 80), new string('c', 80));
        var longPathPrefixed = @"\\?\" + nested;
        Directory.CreateDirectory(longPathPrefixed);
        EvtxFolderFixtures.WriteEmptyFile(longPathPrefixed, "longpath.evtx");

        var result = EvtxFolderEnumerator.EnumerateEvtx(longPathPrefixed, includeSubfolders: false, TestContext.Current.CancellationToken);

        var success = Assert.IsType<EvtxEnumerationResult.Success>(result);
        Assert.Single(success.Files);
        Assert.EndsWith("longpath.evtx", success.Files[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EnumerateEvtx_OnNonexistentFolder_ReturnsIoErrorVariant()
    {
        var nonexistent = Path.Combine(_tempRoot, "does-not-exist");

        var result = EvtxFolderEnumerator.EnumerateEvtx(nonexistent, includeSubfolders: false, TestContext.Current.CancellationToken);

        Assert.IsType<EvtxEnumerationResult.IoError>(result);
    }

    [Fact]
    public void EnumerateEvtx_RejectsNullOrWhitespace()
    {
        Assert.Throws<ArgumentException>(() => EvtxFolderEnumerator.EnumerateEvtx("", includeSubfolders: false, TestContext.Current.CancellationToken));
        Assert.Throws<ArgumentException>(() => EvtxFolderEnumerator.EnumerateEvtx("   ", includeSubfolders: false, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void EnumerateEvtx_WithCancelledToken_ThrowsOperationCanceled()
    {
        EvtxFolderFixtures.WriteEmptyFile(_tempRoot, "a.evtx");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(
            () => EvtxFolderEnumerator.EnumerateEvtx(_tempRoot, includeSubfolders: false, cts.Token));
    }

    [Fact]
    public void EnumerateEvtx_WithSubfolders_ReturnsNestedAndTopLevelEvtx()
    {
        var nested = Path.Combine(_tempRoot, "sub", "deeper");
        Directory.CreateDirectory(nested);
        EvtxFolderFixtures.WriteEmptyFile(nested, "nested.evtx");
        EvtxFolderFixtures.WriteEmptyFile(_tempRoot, "top.evtx");
        EvtxFolderFixtures.WriteEmptyFile(_tempRoot, "ignored.txt");

        var result = EvtxFolderEnumerator.EnumerateEvtx(_tempRoot, includeSubfolders: true, TestContext.Current.CancellationToken);

        var success = Assert.IsType<EvtxEnumerationResult.Success>(result);
        Assert.Equal(2, success.Files.Count);
        Assert.Contains(success.Files, file => file.EndsWith("top.evtx", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(success.Files, file => file.EndsWith("nested.evtx", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ToAlertCopy_OnEmpty_ReturnsNull()
    {
        var result = EvtxFolderEnumerator.EnumerateEvtx(_tempRoot, includeSubfolders: false, TestContext.Current.CancellationToken);

        Assert.Null(EvtxFolderEnumerator.ToAlertCopy(result));
    }

    [Fact]
    public void ToAlertCopy_OnSuccess_ReturnsNull()
    {
        EvtxFolderFixtures.WriteEmptyFile(_tempRoot, "a.evtx");
        var result = EvtxFolderEnumerator.EnumerateEvtx(_tempRoot, includeSubfolders: false, TestContext.Current.CancellationToken);

        Assert.Null(EvtxFolderEnumerator.ToAlertCopy(result));
    }
}
