// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Windows.Tests.TestUtils;
using EventLogExpert.Windows.Tests.TestUtils.Constants;
using EventLogExpert.WindowsPlatform;
using Xunit;

namespace EventLogExpert.Windows.Tests;

public sealed class ActivationTokenClassifierTests
{
    [Fact]
    public void Classify_AcceptsOnlyEvtxFiles_CaseInsensitive()
    {
        var tokens = new[]
        {
            Constants.LowercaseEvtxFile,
            Constants.UppercaseEvtxFile,
            Constants.TxtFile,
            Constants.LogFile,
            Constants.NotesFile,
        };

        var result = ActivationTokenClassifier.Classify(
            tokens,
            ActivationFixtures.CreateFileExistsForExtensions(Constants.EvtxExtension, ".txt", ".log"),
            ActivationFixtures.NeverDirExists);

        Assert.Equal(2, result.EvtxFiles.Count);
        Assert.Contains(Constants.LowercaseEvtxFile, result.EvtxFiles);
        Assert.Contains(Constants.UppercaseEvtxFile, result.EvtxFiles);
    }

    [Fact]
    public void Classify_BucketsFolders_Separately()
    {
        var tokens = new[] { Constants.LogsFolder, Constants.SampleEvtxFile };

        var result = ActivationTokenClassifier.Classify(
            tokens,
            ActivationFixtures.AcceptOnlyEvtxFiles(),
            ActivationFixtures.CreateDirExistsForPath(Constants.LogsFolder));

        Assert.Single(result.EvtxFiles);
        Assert.Single(result.Folders);
        Assert.Equal(Constants.LogsFolder, result.Folders[0]);
        Assert.Equal(Constants.SampleEvtxFile, result.EvtxFiles[0]);
    }

    [Fact]
    public void Classify_CatchesProbeExceptionsAndContinues()
    {
        var tokens = new[] { Constants.ProbeFailurePath, Constants.RealEvtxFile };

        var result = ActivationTokenClassifier.Classify(
            tokens,
            ActivationFixtures.CreateFileExistsThatThrowsFor(
                Constants.ProbeFailurePath,
                new IOException("simulated probe failure"),
                Constants.RealEvtxFile),
            ActivationFixtures.NeverDirExists);

        Assert.Single(result.EvtxFiles);
        Assert.Equal(Constants.RealEvtxFile, result.EvtxFiles[0]);
    }

    [Fact]
    public void Classify_DropsNonexistentTokensSilently()
    {
        var tokens = new[] { Constants.NonexistentEvtxFile, Constants.RealEvtxFile };

        var result = ActivationTokenClassifier.Classify(
            tokens,
            ActivationFixtures.CreateFileExistsForPath(Constants.RealEvtxFile),
            ActivationFixtures.NeverDirExists);

        Assert.Single(result.EvtxFiles);
        Assert.Equal(Constants.RealEvtxFile, result.EvtxFiles[0]);
    }

    [Fact]
    public void Classify_DropsWhitespaceAndNullTokens()
    {
        var tokens = new[] { "", "   ", "\t", Constants.RealEvtxFile, null! };

        var result = ActivationTokenClassifier.Classify(
            tokens,
            ActivationFixtures.CreateFileExistsForPath(Constants.RealEvtxFile),
            ActivationFixtures.NeverDirExists);

        Assert.Single(result.EvtxFiles);
    }

    [Fact]
    public void Classify_EmptyInput_ReturnsEmptyClassification()
    {
        var result = ActivationTokenClassifier.Classify([], ActivationFixtures.AlwaysFileExists, _ => true);

        Assert.Empty(result.EvtxFiles);
        Assert.Empty(result.Folders);
    }

    [Fact]
    public void Classify_RejectsNullArguments()
    {
        Assert.Throws<ArgumentNullException>(() => ActivationTokenClassifier.Classify(null!, ActivationFixtures.AlwaysFileExists, _ => true));
        Assert.Throws<ArgumentNullException>(() => ActivationTokenClassifier.Classify([], null!, _ => true));
        Assert.Throws<ArgumentNullException>(() => ActivationTokenClassifier.Classify([], ActivationFixtures.AlwaysFileExists, null!));
    }

    [Fact]
    public void Classify_SkipsExeTokenWhenLaunchedFromShellExtension()
    {
        var tokens = new[]
        {
            Constants.PackagedExePath,
            Constants.SystemEvtxFile,
            Constants.AppEvtxFile,
        };

        var result = ActivationTokenClassifier.Classify(
            tokens,
            ActivationFixtures.AlwaysFileExists,
            ActivationFixtures.NeverDirExists);

        Assert.Equal(2, result.EvtxFiles.Count);
        Assert.DoesNotContain(result.EvtxFiles, p => p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
        Assert.All(result.EvtxFiles, p => Assert.EndsWith(Constants.EvtxExtension, p, StringComparison.OrdinalIgnoreCase));
    }
}
