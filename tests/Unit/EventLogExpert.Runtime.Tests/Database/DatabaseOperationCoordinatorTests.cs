// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Banner;
using EventLogExpert.Runtime.Common.Files;
using EventLogExpert.Runtime.Database;
using EventLogExpert.Runtime.Database.Upgrade;
using EventLogExpert.Runtime.EventLog;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace EventLogExpert.Runtime.Tests.Database;

public sealed class DatabaseOperationCoordinatorTests
{
    private readonly IDatabaseService _databases = Substitute.For<IDatabaseService>();
    private readonly IErrorBannerService _errorBanners = Substitute.For<IErrorBannerService>();
    private readonly IFilePickerService _filePicker = Substitute.For<IFilePickerService>();
    private readonly IInfoBannerService _infoBanners = Substitute.For<IInfoBannerService>();
    private readonly ILogReloadCoordinator _logReload = Substitute.For<ILogReloadCoordinator>();
    private readonly ITraceLogger _logger = Substitute.For<ITraceLogger>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ApplyPendingTogglesAsync_CancelledMidIteration_ThrowsAndDoesNotSilentlyApplyPrefix()
    {
        using var cts = new CancellationTokenSource();

        _databases.When(x => x.Toggle("a.db")).Do(_ => cts.Cancel());

        var sut = CreateSut();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            sut.ApplyPendingTogglesAsync(["a.db", "b.db", "c.db"], cts.Token));

        _databases.Received(1).Toggle("a.db");
        _databases.DidNotReceive().Toggle("b.db");
        _databases.DidNotReceive().Toggle("c.db");
    }

    [Fact]
    public async Task ApplyPendingTogglesAsync_EmptyInput_NoToggleCallsAndNoBanners()
    {
        var sut = CreateSut();
        await sut.ApplyPendingTogglesAsync([], Ct);

        _databases.DidNotReceiveWithAnyArgs().Toggle(null!);
        _errorBanners.DidNotReceiveWithAnyArgs().ReportError(null!);
    }

    [Fact]
    public async Task ApplyPendingTogglesAsync_FiveFilesWithTwoFailures_AllAttemptedAndOneBannerPerFailure()
    {
        _databases.When(x => x.Toggle("c.db")).Do(_ => throw new InvalidOperationException("c failed"));
        _databases.When(x => x.Toggle("e.db")).Do(_ => throw new InvalidOperationException("e failed"));

        var sut = CreateSut();
        await sut.ApplyPendingTogglesAsync(["a.db", "b.db", "c.db", "d.db", "e.db"], Ct);

        _databases.Received(1).Toggle("a.db");
        _databases.Received(1).Toggle("b.db");
        _databases.Received(1).Toggle("c.db");
        _databases.Received(1).Toggle("d.db");
        _databases.Received(1).Toggle("e.db");
        _errorBanners.Received(1).ReportError(Arg.Is<BannerMessage>(message =>
            IsDatabaseOperationFailed(message, new DatabaseOperation.Toggle("c.db"), "c failed")));
        _errorBanners.Received(1).ReportError(Arg.Is<BannerMessage>(message =>
            IsDatabaseOperationFailed(message, new DatabaseOperation.Toggle("e.db"), "e failed")));
        _errorBanners.Received(2).ReportError(Arg.Any<BannerMessage>());
    }

    [Fact]
    public async Task ApplyPendingTogglesAsync_PreCancelledToken_ThrowsBeforeAnyToggleCall()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var sut = CreateSut();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            sut.ApplyPendingTogglesAsync(["a.db", "b.db"], cts.Token));

        _databases.DidNotReceiveWithAnyArgs().Toggle(null!);
        _errorBanners.DidNotReceiveWithAnyArgs().ReportError(null!);
    }

    [Fact]
    public void BuildImportSummary_AllFailedZeroImported_PreservesFailurePayloadForLocalization()
    {
        var failures = new[] { new ImportFailure("A.db", "bad") };
        var result = new ImportResult(0, [], failures, []);

        var summary = DatabaseOperationCoordinator.BuildImportSummary(result);

        Assert.Equal(0, summary.Imported);
        Assert.Same(failures, summary.Failures);
        Assert.Empty(summary.UpgradeFailures);
        Assert.Equal(DatabaseImportSeverity.Error, summary.Severity);
    }

    [Theory]
    [InlineData(0, 0, 0, (int)DatabaseImportSeverity.Info)]
    [InlineData(1, 0, 0, (int)DatabaseImportSeverity.Info)]
    [InlineData(3, 0, 0, (int)DatabaseImportSeverity.Info)]
    [InlineData(2, 1, 0, (int)DatabaseImportSeverity.Warning)]
    [InlineData(2, 0, 1, (int)DatabaseImportSeverity.Warning)]
    [InlineData(0, 1, 0, (int)DatabaseImportSeverity.Error)]
    public void BuildImportSummary_MapsSeverityCorrectly(
        int imported, int failureCount, int upgradeFailureCount,
        int expectedSeverityAsInt)
    {
        var expectedSeverity = (DatabaseImportSeverity)expectedSeverityAsInt;
        var failures = Enumerable.Range(0, failureCount)
            .Select(i => new ImportFailure($"f{i}.db", "reason")).ToList();
        var upgradeFailures = Enumerable.Range(0, upgradeFailureCount)
            .Select(i => new ImportFailure($"u{i}.db", "reason")).ToList();
        var result = new ImportResult(imported, [], failures, upgradeFailures);

        var summary = DatabaseOperationCoordinator.BuildImportSummary(result);

        Assert.Equal(expectedSeverity, summary.Severity);
        Assert.Equal(imported, summary.Imported);
        Assert.Equal(failures, summary.Failures);
        Assert.Equal(upgradeFailures, summary.UpgradeFailures);
    }

    [Fact]
    public async Task ImportAsync_CallbackReturnsTrue_FileNotAddedToSkipSet()
    {
        _filePicker.PickMultipleAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>())
            .Returns(["/path/exists.db"]);
        _databases.Entries.Returns([CreateEntry("exists.db")]);
        _databases.ImportAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<IReadOnlySet<string>>(), Arg.Any<CancellationToken>())
            .Returns(new ImportResult(1, ["A.db"], [], []));

        var sut = CreateSut();
        await sut.ImportAsync((_, _) => Task.FromResult(true), Ct);

        await _databases.Received(1).ImportAsync(
            Arg.Any<IEnumerable<string>>(),
            Arg.Is<IReadOnlySet<string>>(s => s != null && !s.Contains("exists.db")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ImportAsync_CallbackThrowsNonCancellation_DefensiveSkipAndImportProceeds()
    {
        _filePicker.PickMultipleAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>())
            .Returns(["/path/exists.db"]);
        _databases.Entries.Returns([CreateEntry("exists.db")]);
        _databases.ImportAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<IReadOnlySet<string>>(), Arg.Any<CancellationToken>())
            .Returns(new ImportResult(0, [], [], []));

        Task<bool> ThrowingCallback(string _, CancellationToken __) =>
            Task.FromException<bool>(new InvalidOperationException("ui callback failed"));

        var sut = CreateSut();
        await sut.ImportAsync(ThrowingCallback, Ct);

        // Conflict defaults to Skip because overwriting is riskier than importing nothing.
        await _databases.Received(1).ImportAsync(
            Arg.Any<IEnumerable<string>>(),
            Arg.Is<IReadOnlySet<string>>(s => s != null && s.Contains("exists.db")),
            Arg.Any<CancellationToken>());
        _errorBanners.DidNotReceiveWithAnyArgs().ReportError(null!);
    }

    [Fact]
    public async Task ImportAsync_DatabaseServiceThrowsOperationCanceled_NoBannerAndReturnsNone()
    {
        _filePicker.PickMultipleAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>())
            .Returns(["/path/A.db"]);
        _databases.Entries.Returns([]);
        _databases.ImportAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<IReadOnlySet<string>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        var sut = CreateSut();
        var outcome = await sut.ImportAsync(cancellationToken: Ct);

        _infoBanners.DidNotReceiveWithAnyArgs().ReportInfoBanner(null!, default);
        _errorBanners.DidNotReceiveWithAnyArgs().ReportError(null!);
        Assert.Equal(ImportOutcome.None, outcome);
    }

    [Fact]
    public async Task ImportAsync_DatabaseServiceThrows_RoutesErrorBannerAndReturnsNone()
    {
        _filePicker.PickMultipleAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>())
            .Returns(["/path/A.db"]);
        _databases.Entries.Returns([]);
        _databases.ImportAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<IReadOnlySet<string>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("boom"));

        var sut = CreateSut();
        var outcome = await sut.ImportAsync(cancellationToken: Ct);

        _errorBanners.Received(1).ReportError(Arg.Is<BannerMessage>(message =>
            IsDatabaseOperationFailed(message, new DatabaseOperation.Import(), "boom")));
        _infoBanners.DidNotReceiveWithAnyArgs().ReportInfoBanner(null!, default);
        Assert.Equal(ImportOutcome.None, outcome);
    }

    [Fact]
    public async Task ImportAsync_LongRunningImport_PostOpRoutesDirectlyToInfoBannerRegardlessOfCallerLifetime()
    {
        // Post-import info routes directly to the banner so modal lifetime cannot suppress the summary.
        var importTcs = new TaskCompletionSource<ImportResult>();
        _filePicker.PickMultipleAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>())
            .Returns(["/path/A.db"]);
        _databases.Entries.Returns([]);
        _databases.ImportAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<IReadOnlySet<string>>(), Arg.Any<CancellationToken>())
            .Returns(importTcs.Task);

        var sut = CreateSut();
        var importTask = sut.ImportAsync(cancellationToken: Ct);
        Assert.False(importTask.IsCompleted);
        _infoBanners.DidNotReceiveWithAnyArgs().ReportInfoBanner(null!, default);

        importTcs.SetResult(new ImportResult(1, ["A.db"], [], []));
        var outcome = await importTask;

        _infoBanners.Received(1).ReportInfoBanner(
            Arg.Is<BannerMessage>(message =>
                IsDatabaseImportSummary(message, 1, 0, 0, DatabaseImportSeverity.Info)),
            BannerSeverity.Info);
        Assert.Equal(1, outcome.ImportedCount);
    }

    [Fact]
    public async Task ImportAsync_NoFilesPicked_ReturnsNoneAndDoesNotInvokeDatabaseOrBanners()
    {
        _filePicker.PickMultipleAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>())
            .Returns([]);

        var sut = CreateSut();
        var outcome = await sut.ImportAsync(cancellationToken: Ct);

        Assert.Equal(ImportOutcome.None, outcome);
        await _databases.DidNotReceive().ImportAsync(
            Arg.Any<IEnumerable<string>>(), Arg.Any<IReadOnlySet<string>>(), Arg.Any<CancellationToken>());
        _infoBanners.DidNotReceiveWithAnyArgs().ReportInfoBanner(null!, default);
        _errorBanners.DidNotReceiveWithAnyArgs().ReportError(null!);
    }

    [Fact]
    public async Task ImportAsync_NullCallbackWithExistingConflict_FileAddedToSkipSet()
    {
        _filePicker.PickMultipleAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>())
            .Returns(["/path/exists.db"]);
        _databases.Entries.Returns([CreateEntry("exists.db")]);
        _databases.ImportAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<IReadOnlySet<string>>(), Arg.Any<CancellationToken>())
            .Returns(new ImportResult(0, [], [], []));

        var sut = CreateSut();
        await sut.ImportAsync(askOverwriteAsync: null, cancellationToken: Ct);

        await _databases.Received(1).ImportAsync(
            Arg.Any<IEnumerable<string>>(),
            Arg.Is<IReadOnlySet<string>>(s => s != null && s.Contains("exists.db")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ImportAsync_OverwriteCallbackThrowsOperationCanceled_AbortsImportNoBannerAndReturnsNone()
    {
        _filePicker.PickMultipleAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>())
            .Returns(["/path/exists.db"]);
        _databases.Entries.Returns([CreateEntry("exists.db")]);

        Task<bool> CancellingCallback(string _, CancellationToken __) =>
            Task.FromException<bool>(new OperationCanceledException());

        var sut = CreateSut();
        var outcome = await sut.ImportAsync(CancellingCallback, Ct);

        await _databases.DidNotReceiveWithAnyArgs().ImportAsync(null!, null!, Ct);
        _errorBanners.DidNotReceiveWithAnyArgs().ReportError(null!);
        _infoBanners.DidNotReceiveWithAnyArgs().ReportInfoBanner(null!, default);
        Assert.Equal(ImportOutcome.None, outcome);
    }

    [Fact]
    public async Task ImportAsync_PreCancelledToken_ThrowsBeforeFilePickerOrConflictResolution()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var sut = CreateSut();

        await Assert.ThrowsAsync<OperationCanceledException>(() => sut.ImportAsync(cancellationToken: cts.Token));

        await _filePicker.DidNotReceiveWithAnyArgs().PickMultipleAsync(null!, null!);
        await _databases.DidNotReceiveWithAnyArgs().ImportAsync(null!, null!, Ct);
        _infoBanners.DidNotReceiveWithAnyArgs().ReportInfoBanner(null!, default);
        _errorBanners.DidNotReceiveWithAnyArgs().ReportError(null!);
    }

    [Fact]
    public async Task ImportAsync_SameBasenameSourcesInOneBatch_AddsNameToSkipSet()
    {
        _filePicker.PickMultipleAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>())
            .Returns([@"C:\first\A.db", @"C:\second\a.DB"]);
        _databases.Entries.Returns([]);
        _databases.ImportAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<IReadOnlySet<string>>(), Arg.Any<CancellationToken>())
            .Returns(new ImportResult(0, [], [], []));

        var sut = CreateSut();
        await sut.ImportAsync(askOverwriteAsync: null, cancellationToken: Ct);

        await _databases.Received(1).ImportAsync(
            Arg.Any<IEnumerable<string>>(),
            Arg.Is<IReadOnlySet<string>>(skipFileNames => skipFileNames != null && skipFileNames.Contains("A.db")),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(1, 0, 0, BannerSeverity.Info)]
    [InlineData(3, 0, 0, BannerSeverity.Info)]
    [InlineData(2, 1, 0, BannerSeverity.Warning)]
    [InlineData(2, 0, 1, BannerSeverity.Warning)]
    public async Task ImportAsync_SuccessAndPartial_RoutesInfoBannerWithExpectedSeverity(
        int imported, int failureCount, int upgradeFailureCount,
        BannerSeverity expectedSeverity)
    {
        _filePicker.PickMultipleAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>())
            .Returns(["/path/A.db"]);
        _databases.Entries.Returns([]);
        var failures = Enumerable.Range(0, failureCount).Select(i => new ImportFailure($"f{i}.db", "x")).ToList();
        var upgradeFailures = Enumerable.Range(0, upgradeFailureCount).Select(i => new ImportFailure($"u{i}.db", "x")).ToList();
        _databases.ImportAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<IReadOnlySet<string>>(), Arg.Any<CancellationToken>())
            .Returns(new ImportResult(imported, [], failures, upgradeFailures));

        var sut = CreateSut();
        var outcome = await sut.ImportAsync(cancellationToken: Ct);

        _infoBanners.Received(1).ReportInfoBanner(
            Arg.Is<BannerMessage>(message =>
                IsDatabaseImportSummary(message, imported, failureCount, upgradeFailureCount)),
            expectedSeverity);
        _errorBanners.DidNotReceiveWithAnyArgs().ReportError(null!);
        Assert.Equal(imported, outcome.ImportedCount);
        Assert.True(outcome.DatabaseStateChanged);
    }

    [Fact]
    public async Task ImportAsync_TokenCancelledDuringConflictResolution_AbortsBeforeReachingDatabaseImport()
    {
        _filePicker.PickMultipleAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>())
            .Returns(["/path/exists1.db", "/path/exists2.db", "/path/exists3.db"]);
        _databases.Entries.Returns(
        [
            CreateEntry("exists1.db"),
            CreateEntry("exists2.db"),
            CreateEntry("exists3.db"),
        ]);

        using var cts = new CancellationTokenSource();

        var promptedNames = new List<string>();
        Task<bool> CancelOnFirstPrompt(string candidateName, CancellationToken _)
        {
            promptedNames.Add(candidateName);
            cts.Cancel();

            return Task.FromResult(false);
        }

        var sut = CreateSut();
        var outcome = await sut.ImportAsync(CancelOnFirstPrompt, cts.Token);

        await _databases.DidNotReceiveWithAnyArgs().ImportAsync(null!, null!, Ct);
        Assert.Equal(ImportOutcome.None, outcome);
        Assert.Single(promptedNames);
    }

    [Fact]
    public async Task ImportAsync_ZeroImportedWithFailures_RoutesErrorBannerNotInfoBanner()
    {
        _filePicker.PickMultipleAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>())
            .Returns(["/path/A.db"]);
        _databases.Entries.Returns([]);
        _databases.ImportAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<IReadOnlySet<string>>(), Arg.Any<CancellationToken>())
            .Returns(new ImportResult(0, [], [new ImportFailure("A.db", "bad")], []));

        var sut = CreateSut();
        var outcome = await sut.ImportAsync(cancellationToken: Ct);

        _errorBanners.Received(1).ReportError(Arg.Is<BannerMessage>(message =>
            IsFailedDatabaseImportSummary(message, "A.db", "bad")));
        _infoBanners.DidNotReceiveWithAnyArgs().ReportInfoBanner(null!, default);
        Assert.Equal(0, outcome.ImportedCount);
        Assert.False(outcome.DatabaseStateChanged);
    }

    [Fact]
    public async Task ImportPathsAsync_EnableOnImportFalse_ImportsGivenPathsWithoutPickerAndLeavesImportedEntryDisabled()
    {
        _databases.Entries.Returns([CreateEntry("A.db", isEnabled: false)]);
        _databases.ImportAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<IReadOnlySet<string>>(), Arg.Any<CancellationToken>())
            .Returns(new ImportResult(1, ["A.db"], [], []));

        var sut = CreateSut();
        var outcome = await sut.ImportPathsAsync([@"C:\out\A.db"], enableOnImport: false, cancellationToken: Ct);

        Assert.Equal(1, outcome.ImportedCount);
        await _filePicker.DidNotReceiveWithAnyArgs().PickMultipleAsync(null!, null!);
        await _databases.Received(1).ImportAsync(
            Arg.Is<IEnumerable<string>>(paths => paths != null && paths.SequenceEqual(new[] { @"C:\out\A.db" })),
            Arg.Any<IReadOnlySet<string>>(),
            Arg.Any<CancellationToken>());
        _databases.DidNotReceiveWithAnyArgs().Toggle(null!);
        await _logReload.DidNotReceiveWithAnyArgs().ReloadAllActiveLogsAsync(Ct);
    }

    [Fact]
    public async Task ImportPathsAsync_EnableOnImportTrue_DoesNotEnableImportedEntryWhenNotReady()
    {
        _databases.Entries.Returns([CreateEntry("A.db", isEnabled: false, status: DatabaseStatus.UpgradeRequired)]);
        _databases.ImportAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<IReadOnlySet<string>>(), Arg.Any<CancellationToken>())
            .Returns(new ImportResult(1, ["A.db"], [], []));
        _logReload.HasActiveLogs.Returns(true);

        var sut = CreateSut();
        var outcome = await sut.ImportPathsAsync([@"C:\out\A.db"], enableOnImport: true, cancellationToken: Ct);

        Assert.Equal(1, outcome.ImportedCount);
        _databases.DidNotReceiveWithAnyArgs().Toggle(null!);
        await _logReload.DidNotReceiveWithAnyArgs().ReloadAllActiveLogsAsync(Ct);
    }

    [Fact]
    public async Task ImportPathsAsync_EnableOnImportTrue_EnablesFreshReadyEntriesAndReloadsOpenLogs()
    {
        _databases.Entries.Returns([CreateEntry("A.db", isEnabled: false)]);
        _databases.ImportAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<IReadOnlySet<string>>(), Arg.Any<CancellationToken>())
            .Returns(new ImportResult(1, ["A.db"], [], []));
        _logReload.HasActiveLogs.Returns(true);

        var sut = CreateSut();
        var outcome = await sut.ImportPathsAsync([@"C:\out\A.db"], enableOnImport: true, cancellationToken: Ct);

        Assert.Equal(1, outcome.ImportedCount);
        _databases.Received(1).Toggle("A.db");
        await _logReload.Received(1).ReloadAllActiveLogsAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoveDatabaseAsync_DatabaseThrowsAfterSnapshotPopulated_SnapshotReopensAndErrorBannerReports()
    {
        // RemoveDatabaseAsync must reopen after both catch paths even though the shared helper swallows exceptions.
        _databases.Entries.Returns([CreateEntry("a.db")]);
        _databases.RemoveAsync("a.db", Arg.Any<Func<CancellationToken, Task>?>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                var prepare = call.ArgAt<Func<CancellationToken, Task>?>(1);
                if (prepare is not null) { await prepare(CancellationToken.None); }
                throw new InvalidOperationException("removal failed mid-flight");
            });
        _logReload
            .When(x => x.PrepareForDatabaseRemovalAsync(Arg.Any<LogReopenSnapshot>(), Arg.Any<CancellationToken>()))
            .Do(call => call.ArgAt<LogReopenSnapshot>(0).Add(new LogReopenInfo("App", LogPathType.Channel)));

        var sut = CreateSut();
        var outcome = await sut.RemoveDatabaseAsync("a.db", (_, _) => Task.FromResult(true), Ct);

        Assert.Equal(RemoveOutcomeStatus.Confirmed, outcome.Status);
        Assert.False(outcome.Removed);
        Assert.True(outcome.LogsReopened);
        _logReload.Received(1).ReopenAfterDatabaseRemoval(Arg.Any<IReadOnlyList<LogReopenInfo>>());
        _errorBanners.Received(1).ReportError(Arg.Is<BannerMessage>(message =>
            IsDatabaseRemoveFailed(message, "a.db", "removal failed mid-flight")));
    }

    [Fact]
    public async Task RemoveDatabaseAsync_DatabaseThrowsOperationCanceledAfterSnapshotPopulated_SnapshotReopensWithoutErrorBanner()
    {
        _databases.Entries.Returns([CreateEntry("a.db")]);
        _databases.RemoveAsync("a.db", Arg.Any<Func<CancellationToken, Task>?>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                var prepare = call.ArgAt<Func<CancellationToken, Task>?>(1);
                if (prepare is not null) { await prepare(CancellationToken.None); }
                throw new OperationCanceledException();
            });
        _logReload
            .When(x => x.PrepareForDatabaseRemovalAsync(Arg.Any<LogReopenSnapshot>(), Arg.Any<CancellationToken>()))
            .Do(call => call.ArgAt<LogReopenSnapshot>(0).Add(new LogReopenInfo("App", LogPathType.Channel)));

        var sut = CreateSut();
        var outcome = await sut.RemoveDatabaseAsync("a.db", (_, _) => Task.FromResult(true), Ct);

        Assert.False(outcome.Removed);
        Assert.True(outcome.LogsReopened);
        _logReload.Received(1).ReopenAfterDatabaseRemoval(Arg.Any<IReadOnlyList<LogReopenInfo>>());
        _errorBanners.DidNotReceiveWithAnyArgs().ReportError(null!);
    }

    [Fact]
    public async Task RemoveDatabaseAsync_FileNotInRegistry_ReturnsNotFoundWithoutDatabaseCall()
    {
        _databases.Entries.Returns([]);

        var sut = CreateSut();
        var outcome = await sut.RemoveDatabaseAsync("missing.db", (_, _) => Task.FromResult(true), Ct);

        Assert.Equal(RemoveOutcomeStatus.NotFound, outcome.Status);
        Assert.False(outcome.Confirmed);
        Assert.False(outcome.Removed);
        await _databases.DidNotReceiveWithAnyArgs().RemoveAsync(null!, null, Ct);
    }

    [Fact]
    public async Task RemoveDatabaseAsync_NullCallback_ThrowsArgumentNullException()
    {
        var sut = CreateSut();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            sut.RemoveDatabaseAsync("a.db", null!, Ct));
    }

    [Fact]
    public async Task RemoveDatabaseAsync_PreCancelledToken_ThrowsBeforeInvokingConfirmCallback()
    {
        _databases.Entries.Returns([CreateEntry("a.db")]);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var callbackInvocations = 0;

        var sut = CreateSut();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            sut.RemoveDatabaseAsync(
                "a.db",
                (_, _) =>
                {
                    callbackInvocations++;
                    return Task.FromResult(true);
                },
                cts.Token));

        Assert.Equal(0, callbackInvocations);
        await _databases.DidNotReceiveWithAnyArgs().RemoveAsync(null!, null, Ct);
        _errorBanners.DidNotReceiveWithAnyArgs().ReportError(null!);
    }

    [Theory]
    [InlineData("callback-false")]
    [InlineData("callback-oce")]
    [InlineData("callback-throws-non-oce")]
    public async Task RemoveDatabaseAsync_RefusedConfirmation_ReturnsNotConfirmedAndDoesNotCallDatabase(string scenario)
    {
        _databases.Entries.Returns([CreateEntry("a.db")]);

        Func<bool, CancellationToken, Task<bool>> callback = scenario switch
        {
            "callback-false" => (_, _) => Task.FromResult(false),
            "callback-oce" => (_, _) => Task.FromException<bool>(new OperationCanceledException()),
            "callback-throws-non-oce" => (_, _) => Task.FromException<bool>(new InvalidOperationException("ui failed")),
            _ => throw new InvalidOperationException(scenario),
        };

        var sut = CreateSut();
        var outcome = await sut.RemoveDatabaseAsync("a.db", callback, Ct);

        Assert.Equal(RemoveOutcomeStatus.NotConfirmed, outcome.Status);
        await _databases.DidNotReceiveWithAnyArgs().RemoveAsync(null!, null, Ct);
        _logReload.DidNotReceiveWithAnyArgs().ReopenAfterDatabaseRemoval(null!);
        _errorBanners.DidNotReceiveWithAnyArgs().ReportError(null!);
    }

    [Fact]
    public async Task RemoveDatabaseAsync_SuccessNoClosedLogs_ReturnsRemovedNoReopen()
    {
        _databases.Entries.Returns([CreateEntry("a.db")]);
        _databases.RemoveAsync("a.db", Arg.Any<Func<CancellationToken, Task>?>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sut = CreateSut();
        var outcome = await sut.RemoveDatabaseAsync("a.db", (_, _) => Task.FromResult(true), Ct);

        Assert.Equal(RemoveOutcomeStatus.Confirmed, outcome.Status);
        Assert.True(outcome.Removed);
        Assert.False(outcome.LogsReopened);
        await _databases.Received(1).RemoveAsync("a.db", Arg.Any<Func<CancellationToken, Task>?>(), Arg.Any<CancellationToken>());
        _logReload.DidNotReceiveWithAnyArgs().ReopenAfterDatabaseRemoval(null!);
    }

    [Fact]
    public async Task RemoveDatabaseAsync_SuccessWithClosedLogs_ReopensLogsAndReportsReopened()
    {
        _databases.Entries.Returns([CreateEntry("a.db")]);
        _databases.RemoveAsync("a.db", Arg.Any<Func<CancellationToken, Task>?>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                var prepare = call.ArgAt<Func<CancellationToken, Task>?>(1);
                if (prepare is not null) { await prepare(CancellationToken.None); }
            });
        _logReload
            .When(x => x.PrepareForDatabaseRemovalAsync(Arg.Any<LogReopenSnapshot>(), Arg.Any<CancellationToken>()))
            .Do(call => call.ArgAt<LogReopenSnapshot>(0).Add(new LogReopenInfo("Application", LogPathType.Channel)));

        var sut = CreateSut();
        var outcome = await sut.RemoveDatabaseAsync("a.db", (_, _) => Task.FromResult(true), Ct);

        Assert.True(outcome.Removed);
        Assert.True(outcome.LogsReopened);
        _logReload.Received(1).ReopenAfterDatabaseRemoval(
            Arg.Is<IReadOnlyList<LogReopenInfo>>(l => l != null && l.Count == 1 && l[0].Name == "Application"));
        _errorBanners.DidNotReceiveWithAnyArgs().ReportError(null!);
    }

    [Theory]
    [InlineData(true, DatabaseStatus.Ready, true, true)]
    [InlineData(true, DatabaseStatus.Ready, false, false)]
    [InlineData(false, DatabaseStatus.Ready, true, false)]
    [InlineData(true, DatabaseStatus.UpgradeRequired, true, false)]
    public async Task RemoveDatabaseAsync_WarningFlag_DerivesFromEnabledStatusAndActiveLogs(
        bool isEnabled, DatabaseStatus status, bool hasActiveLogs, bool expectedWarning)
    {
        _databases.Entries.Returns([CreateEntry("a.db", isEnabled, status)]);
        _logReload.HasActiveLogs.Returns(hasActiveLogs);

        bool? receivedFlag = null;

        var sut = CreateSut();
        await sut.RemoveDatabaseAsync("a.db", (showWarning, _) =>
        {
            receivedFlag = showWarning;
            return Task.FromResult(false);
        }, Ct);

        Assert.Equal(expectedWarning, receivedFlag);
    }

    [Fact]
    public async Task UpgradeDatabaseAsync_DatabaseThrows_ReportsErrorBannerAndClearsStateAndRaisesExit()
    {
        _databases.UpgradeBatchAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<UpgradeProgressScope>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("upgrade body failed"));

        var sut = CreateSut();
        int stateChangeCount = 0;
        sut.UpgradeStateChanged += () => stateChangeCount++;

        await sut.UpgradeDatabaseAsync("a.db", cancellationToken: Ct);

        Assert.False(sut.IsAnyUpgradeInFlight);
        Assert.Equal(2, stateChangeCount);
        _errorBanners.Received(1).ReportError(Arg.Is<BannerMessage>(message =>
            IsDatabaseOperationFailed(message, new DatabaseOperation.UpgradeSingle("a.db"), "upgrade body failed")));
    }

    [Fact]
    public async Task UpgradeDatabaseAsync_OperationCancelled_NoBannerAndStateCleared()
    {
        _databases.UpgradeBatchAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<UpgradeProgressScope>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        var sut = CreateSut();
        await sut.UpgradeDatabaseAsync("a.db", cancellationToken: Ct);

        Assert.False(sut.IsAnyUpgradeInFlight);
        _errorBanners.DidNotReceiveWithAnyArgs().ReportError(null!);
    }

    [Fact]
    public async Task UpgradeDatabaseAsync_PreCancelledToken_ThrowsBeforeAcquiringLockOrRaisingEvents()
    {
        using var cts = new CancellationTokenSource();

        cts.Cancel();

        var sut = CreateSut();
        int stateChangeCount = 0;
        sut.UpgradeStateChanged += () => stateChangeCount++;

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            sut.UpgradeDatabaseAsync("a.db", cancellationToken: cts.Token));

        Assert.Equal(0, stateChangeCount);
        Assert.False(sut.IsAnyUpgradeInFlight);
        await _databases.DidNotReceiveWithAnyArgs().UpgradeBatchAsync(default!, default, Ct);
    }

    [Fact]
    public async Task UpgradeDatabaseAsync_ResultFailedPopulated_ReportsOneErrorBannerPerFailure()
    {
        _databases.UpgradeBatchAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<UpgradeProgressScope>(), Arg.Any<CancellationToken>())
            .Returns(new UpgradeBatchResult([], [], [
                new UpgradeFailure("a.db", "schema-mismatch"),
                new UpgradeFailure("b.db", "io-error"),
            ]));

        var sut = CreateSut();
        await sut.UpgradeDatabaseAsync("a.db", cancellationToken: Ct);

        _errorBanners.Received(1).ReportError(Arg.Is<BannerMessage>(message =>
            IsDatabaseUpgradeFailed(message, "a.db", "schema-mismatch")));
        _errorBanners.Received(1).ReportError(Arg.Is<BannerMessage>(message =>
            IsDatabaseUpgradeFailed(message, "b.db", "io-error")));
    }

    [Theory]
    [InlineData("a.db")]
    [InlineData("b.db")]
    public async Task UpgradeDatabaseAsync_SecondCallWhileFirstInFlight_NoOpsAndDoesNotInvokeDatabase(string secondFile)
    {
        var upgradeTcs = new TaskCompletionSource<UpgradeBatchResult>();
        _databases.UpgradeBatchAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<UpgradeProgressScope>(), Arg.Any<CancellationToken>())
            .Returns(upgradeTcs.Task);

        var sut = CreateSut();
        var firstCall = sut.UpgradeDatabaseAsync("a.db", cancellationToken: Ct);
        Assert.True(sut.IsUpgradeInFlight("a.db"));

        await sut.UpgradeDatabaseAsync(secondFile, cancellationToken: Ct);

        Assert.True(sut.IsUpgradeInFlight("a.db"));
        if (secondFile != "a.db") { Assert.False(sut.IsUpgradeInFlight(secondFile)); }

        upgradeTcs.SetResult(new UpgradeBatchResult([], [], []));
        await firstCall;

        await _databases.Received(1).UpgradeBatchAsync(
            Arg.Any<IReadOnlyList<string>>(), Arg.Any<UpgradeProgressScope>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpgradeDatabaseAsync_StateChangedSubscriberThrows_StateStillClearedAndUpgradeBodyRan()
    {
        // One bad subscriber must not wedge in-flight state or skip the upgrade body.
        _databases.UpgradeBatchAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<UpgradeProgressScope>(), Arg.Any<CancellationToken>())
            .Returns(new UpgradeBatchResult([], [], []));

        var sut = CreateSut();
        sut.UpgradeStateChanged += () => throw new InvalidOperationException("subscriber threw");

        await sut.UpgradeDatabaseAsync("a.db", cancellationToken: Ct);

        Assert.False(sut.IsAnyUpgradeInFlight);
        Assert.False(sut.IsUpgradeInFlight("a.db"));
        await _databases.Received(1).UpgradeBatchAsync(
            Arg.Is<IReadOnlyList<string>>(l => l != null && l.Count == 1 && l[0] == "a.db"),
            Arg.Any<UpgradeProgressScope>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpgradeDatabaseAsync_Success_TracksInFlightStateAndRaisesEnterExitEvents()
    {
        _databases.UpgradeBatchAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<UpgradeProgressScope>(), Arg.Any<CancellationToken>())
            .Returns(new UpgradeBatchResult([], [], []));

        var sut = CreateSut();
        bool wasInFlightDuringEnter = false;
        bool wasInFlightDuringExit = false;
        bool enterFired = false;
        sut.UpgradeStateChanged += () =>
        {
            if (!enterFired)
            {
                enterFired = true;
                wasInFlightDuringEnter = sut.IsUpgradeInFlight("a.db");
            }
            else
            {
                wasInFlightDuringExit = sut.IsUpgradeInFlight("a.db");
            }
        };

        await sut.UpgradeDatabaseAsync("a.db", cancellationToken: Ct);

        Assert.True(wasInFlightDuringEnter, "Enter event should fire while file is in-flight");
        Assert.False(wasInFlightDuringExit, "Exit event should fire after file is removed");
        Assert.False(sut.IsAnyUpgradeInFlight);
        await _databases.Received(1).UpgradeBatchAsync(
            Arg.Is<IReadOnlyList<string>>(l => l != null && l.Count == 1 && l[0] == "a.db"),
            UpgradeProgressScope.ManageDatabasesTriggered,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpgradeDatabasesAsync_EmptyInput_ReturnsEmptyResult_DoesNotCallService()
    {
        var sut = CreateSut();

        var result = await sut.UpgradeDatabasesAsync([], UpgradeProgressScope.ManageDatabasesTriggered, Ct);

        Assert.NotNull(result);
        Assert.Empty(result.Succeeded);
        Assert.Empty(result.Cancelled);
        Assert.Empty(result.Failed);
        await _databases.DidNotReceiveWithAnyArgs().UpgradeBatchAsync(default!, default, Ct);
    }

    [Fact]
    public async Task UpgradeDatabasesAsync_GateDenied_AnotherUpgradeInFlight_ReturnsNull()
    {
        var blocker = new TaskCompletionSource<UpgradeBatchResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _databases.UpgradeBatchAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<UpgradeProgressScope>(), Arg.Any<CancellationToken>())
            .Returns(_ => blocker.Task);

        var sut = CreateSut();

        var firstUpgrade = sut.UpgradeDatabaseAsync("a.db", cancellationToken: Ct);
        await Task.Yield();

        Assert.True(sut.IsAnyUpgradeInFlight);

        var result = await sut.UpgradeDatabasesAsync(["b.db", "c.db"], UpgradeProgressScope.ManageDatabasesTriggered, Ct);

        Assert.Null(result);

        blocker.SetResult(new UpgradeBatchResult([], [], []));
        await firstUpgrade;
    }

    [Fact]
    public async Task UpgradeDatabasesAsync_HappyPath_ReturnsServiceResult_AndFiresStateChangedTwice()
    {
        var expected = new UpgradeBatchResult(Succeeded: ["a.db", "b.db"], Cancelled: [], Failed: []);
        _databases.UpgradeBatchAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<UpgradeProgressScope>(), Arg.Any<CancellationToken>())
            .Returns(expected);

        var sut = CreateSut();
        int stateChangedCount = 0;
        sut.UpgradeStateChanged += () => stateChangedCount++;

        var result = await sut.UpgradeDatabasesAsync(["a.db", "b.db"], UpgradeProgressScope.ManageDatabasesTriggered, Ct);

        Assert.Same(expected, result);
        Assert.Equal(2, stateChangedCount);
        Assert.False(sut.IsAnyUpgradeInFlight);
    }

    [Fact]
    public async Task UpgradeDatabasesAsync_PartialFailure_SurfacesEachFailureToErrorBanner()
    {
        var result = new UpgradeBatchResult(
            Succeeded: ["a.db"],
            Cancelled: [],
            Failed: [new UpgradeFailure("b.db", "schema mismatch"), new UpgradeFailure("c.db", "io error")]);
        _databases.UpgradeBatchAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<UpgradeProgressScope>(), Arg.Any<CancellationToken>())
            .Returns(result);

        var sut = CreateSut();
        var actual = await sut.UpgradeDatabasesAsync(["a.db", "b.db", "c.db"], UpgradeProgressScope.ManageDatabasesTriggered, Ct);

        Assert.Same(result, actual);
        _errorBanners.Received(1).ReportError(Arg.Is<BannerMessage>(message =>
            IsDatabaseUpgradeFailed(message, "b.db", "schema mismatch")));
        _errorBanners.Received(1).ReportError(Arg.Is<BannerMessage>(message =>
            IsDatabaseUpgradeFailed(message, "c.db", "io error")));
    }

    [Fact]
    public async Task UpgradeDatabasesAsync_PerFileTracking_AllFilesInFlightDuringCall()
    {
        DatabaseOperationCoordinator? sutRef = null;
        bool aSeen = false, bSeen = false;
        _databases.UpgradeBatchAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<UpgradeProgressScope>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                aSeen = sutRef!.IsUpgradeInFlight("a.db");
                bSeen = sutRef!.IsUpgradeInFlight("b.db");
                return new UpgradeBatchResult([], [], []);
            });

        sutRef = CreateSut();
        await sutRef.UpgradeDatabasesAsync(["a.db", "b.db"], UpgradeProgressScope.ManageDatabasesTriggered, Ct);

        Assert.True(aSeen);
        Assert.True(bSeen);
        Assert.False(sutRef.IsUpgradeInFlight("a.db"));
        Assert.False(sutRef.IsUpgradeInFlight("b.db"));
    }

    [Fact]
    public async Task UpgradeDatabasesAsync_PreCancelledToken_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var sut = CreateSut();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            sut.UpgradeDatabasesAsync(["a.db"], UpgradeProgressScope.ManageDatabasesTriggered, cts.Token));

        Assert.False(sut.IsAnyUpgradeInFlight);
    }

    [Fact]
    public async Task UpgradeDatabasesAsync_ServiceThrows_FallbackReturnedAndInFlightCleared()
    {
        _databases.UpgradeBatchAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<UpgradeProgressScope>(), Arg.Any<CancellationToken>())
            .Returns<UpgradeBatchResult>(_ => throw new InvalidOperationException("disk full"));

        var sut = CreateSut();
        var result = await sut.UpgradeDatabasesAsync(["a.db"], UpgradeProgressScope.ManageDatabasesTriggered, Ct);

        Assert.NotNull(result);
        Assert.Empty(result.Succeeded);
        Assert.False(sut.IsAnyUpgradeInFlight);
        _errorBanners.Received(1).ReportError(Arg.Is<BannerMessage>(message =>
            IsDatabaseOperationFailed(message, new DatabaseOperation.UpgradeBatch(1), "disk full")));
    }

    private static DatabaseEntry CreateEntry(
        string fileName,
        bool isEnabled = true,
        DatabaseStatus status = DatabaseStatus.Ready) =>
        new(fileName, $"C:/db/{fileName}", isEnabled, status);

    private static bool IsDatabaseImportSummary(
        BannerMessage? message,
        int imported,
        int failureCount,
        int upgradeFailureCount,
        DatabaseImportSeverity? severity = null) =>
        message is DatabaseImportSummary summary &&
        summary.Imported == imported &&
        summary.Failures.Count == failureCount &&
        summary.UpgradeFailures.Count == upgradeFailureCount &&
        (severity is null || summary.Severity == severity);

    private static bool IsDatabaseOperationFailed(
        BannerMessage? message,
        DatabaseOperation operation,
        string detail) =>
        message is DatabaseOperationFailed operationFailed &&
        operationFailed.Operation == operation &&
        operationFailed.Detail == detail;

    private static bool IsDatabaseRemoveFailed(BannerMessage? message, string fileName, string detail) =>
        message is DatabaseRemoveFailed removeFailed &&
        removeFailed.FileName == fileName &&
        removeFailed.Detail == detail;

    private static bool IsDatabaseUpgradeFailed(BannerMessage? message, string fileName, string reason) =>
        message is DatabaseUpgradeFailed upgradeFailed &&
        upgradeFailed.FileName == fileName &&
        upgradeFailed.Reason == reason;

    private static bool IsFailedDatabaseImportSummary(BannerMessage? message, string fileName, string reason) =>
        message is DatabaseImportSummary { Imported: 0, Severity: DatabaseImportSeverity.Error } summary &&
        summary.Failures.Count == 1 &&
        summary.Failures[0].FileName == fileName &&
        summary.Failures[0].Reason == reason;

    private DatabaseOperationCoordinator CreateSut() =>
        new(_databases, _infoBanners, _errorBanners, _filePicker, _logReload, _logger);
}
