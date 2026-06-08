// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Windows.Tests.TestUtils;
using EventLogExpert.Windows.Tests.TestUtils.Constants;
using EventLogExpert.WindowsPlatform.Activation;
using Xunit;

namespace EventLogExpert.Windows.Tests;

public sealed class EvtxFolderEnumeratorTests : IDisposable
{
    private readonly string _tempRoot;

    public EvtxFolderEnumeratorTests()
    {
        _tempRoot = EvtxFolderFixtures.CreateTempTestFolder();
    }

    public void Dispose()
    {
        EvtxFolderFixtures.TryDeleteFolder(_tempRoot);
    }

    [Fact]
    public void EnumerateTopLevel_DoesNotRecurseIntoSubfolders()
    {
        var sub = Path.Combine(_tempRoot, "sub");
        Directory.CreateDirectory(sub);
        EvtxFolderFixtures.WriteEmptyFile(sub, "nested.evtx");
        EvtxFolderFixtures.WriteEmptyFile(_tempRoot, "top.evtx");

        var result = EvtxFolderEnumerator.EnumerateEvtxTopLevel(_tempRoot);

        var success = Assert.IsType<EvtxEnumerationResult.Success>(result);
        Assert.Single(success.Files);
        Assert.EndsWith("top.evtx", success.Files[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EnumerateTopLevel_OnEmptyFolder_ReturnsEmptyVariant()
    {
        var result = EvtxFolderEnumerator.EnumerateEvtxTopLevel(_tempRoot);

        Assert.IsType<EvtxEnumerationResult.Empty>(result);
    }

    [Fact]
    public void EnumerateTopLevel_OnFolderWithEvtxAndOtherFiles_ReturnsOnlyEvtx()
    {
        EvtxFolderFixtures.WriteEmptyFile(_tempRoot, "a.evtx");
        EvtxFolderFixtures.WriteEmptyFile(_tempRoot, "b.evtx");
        EvtxFolderFixtures.WriteEmptyFile(_tempRoot, "ignored.txt");
        EvtxFolderFixtures.WriteEmptyFile(_tempRoot, "ignored.log");

        var result = EvtxFolderEnumerator.EnumerateEvtxTopLevel(_tempRoot);

        var success = Assert.IsType<EvtxEnumerationResult.Success>(result);
        Assert.Equal(2, success.Files.Count);
        Assert.All(success.Files, f => Assert.EndsWith(Constants.EvtxExtension, f, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EnumerateTopLevel_OnLongPathBeyondMax_StillReturnsEvtxFiles()
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

        var result = EvtxFolderEnumerator.EnumerateEvtxTopLevel(longPathPrefixed);

        var success = Assert.IsType<EvtxEnumerationResult.Success>(result);
        Assert.Single(success.Files);
        Assert.EndsWith("longpath.evtx", success.Files[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EnumerateTopLevel_OnNonexistentFolder_ReturnsIoErrorVariant()
    {
        var nonexistent = Path.Combine(_tempRoot, "does-not-exist");

        var result = EvtxFolderEnumerator.EnumerateEvtxTopLevel(nonexistent);

        Assert.IsType<EvtxEnumerationResult.IoError>(result);
    }

    [Fact]
    public void EnumerateTopLevel_RejectsNullOrWhitespace()
    {
        Assert.Throws<ArgumentException>(() => EvtxFolderEnumerator.EnumerateEvtxTopLevel(""));
        Assert.Throws<ArgumentException>(() => EvtxFolderEnumerator.EnumerateEvtxTopLevel("   "));
    }

    [Fact]
    public void ToAlertCopy_OnEmpty_ReturnsNull()
    {
        var result = EvtxFolderEnumerator.EnumerateEvtxTopLevel(_tempRoot);

        Assert.Null(EvtxFolderEnumerator.ToAlertCopy(result));
    }

    [Fact]
    public void ToAlertCopy_OnSuccess_ReturnsNull()
    {
        EvtxFolderFixtures.WriteEmptyFile(_tempRoot, "a.evtx");
        var result = EvtxFolderEnumerator.EnumerateEvtxTopLevel(_tempRoot);

        Assert.Null(EvtxFolderEnumerator.ToAlertCopy(result));
    }
}
