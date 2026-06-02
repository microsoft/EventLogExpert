// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Logging.Abstractions.Handlers;
using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.Common.Activation;
using EventLogExpert.Runtime.Common.Threading;
using EventLogExpert.WindowsPlatform;
using NSubstitute;
using System.Threading.Channels;
using Xunit;

namespace EventLogExpert.Windows.Tests;

public sealed class ActivationDispatcherTests
{
    [Fact]
    public async Task Enqueue_OnEmptyArgs_DoesNotInvokeOpenBatch()
    {
        var (dispatcher, channel) = CreateDispatcher();
        var invocations = 0;

        var consumerTask = dispatcher.StartConsumingAsync(
            (_, _) => { Interlocked.Increment(ref invocations); return Task.CompletedTask; },
            TestContext.Current.CancellationToken);

        dispatcher.Enqueue(ActivationArgs.Empty);

        channel.Writer.Complete();
        await consumerTask;

        Assert.Equal(0, invocations);
    }

    [Fact]
    public void Enqueue_OnCompletedChannel_LogsWarning_AndDoesNotThrow()
    {
        var logger = Substitute.For<ITraceLogger>();
        var (dispatcher, channel) = CreateDispatcher(logger: logger);

        channel.Writer.Complete();

        // TryWrite returns false on a completed unbounded channel; Enqueue must observe the
        // rejection and surface it via the logger — silent drop hides "Explorer activation did
        // nothing" failures in the field.
        var exception = Record.Exception(() =>
            dispatcher.Enqueue(new ActivationArgs([@"E:\Data\one.evtx"], [])));

        Assert.Null(exception);
        logger.Received(1).Warning(Arg.Any<WarningLogHandler>());
    }

    [Fact]
    public async Task ProcessBatch_FolderFailureSurfacedAsAlert_BeforeOpenBatch()
    {
        var dialogService = Substitute.For<IAlertDialogService>();
        var mainThread = Substitute.For<IMainThreadService>();
        mainThread.InvokeOnMainThreadAsync(Arg.Any<Func<Task>>())
            .Returns(callInfo => callInfo.Arg<Func<Task>>().Invoke());

        var (dispatcher, channel) = CreateDispatcher(dialogService: dialogService, mainThread: mainThread);
        var alertOrderedBeforeOpen = false;
        var alertSeen = false;
        var openCalled = false;

        dialogService
            .When(d => d.ShowAlert(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()))
            .Do(_ =>
            {
                alertSeen = true;
                if (!openCalled) { alertOrderedBeforeOpen = true; }
            });

        var consumerTask = dispatcher.StartConsumingAsync(
            (_, _) => { openCalled = true; return Task.CompletedTask; },
            TestContext.Current.CancellationToken);

        // Nonexistent folder -> IoError -> ToAlertCopy produces "Open Folder Failed".
        var bogusFolder = Path.Combine(Path.GetTempPath(), "evtx-dispatcher-test-" + Guid.NewGuid().ToString("N"));
        dispatcher.Enqueue(new ActivationArgs([], [bogusFolder]));

        channel.Writer.Complete();
        await consumerTask;

        Assert.True(alertSeen);
        Assert.False(openCalled, "openBatch must not be called when no .evtx files were enumerated");
        Assert.True(alertOrderedBeforeOpen);
    }

    [Fact]
    public async Task ProcessBatch_OnCancellationBeforeOpenBatch_DoesNotInvokeOpenBatch()
    {
        var mainThread = Substitute.For<IMainThreadService>();
        mainThread.InvokeOnMainThreadAsync(Arg.Any<Func<Task>>())
            .Returns(callInfo => callInfo.Arg<Func<Task>>().Invoke());

        var (dispatcher, channel) = CreateDispatcher(mainThread: mainThread);
        var openBatchInvoked = false;
        using var cts = new CancellationTokenSource();

        var consumerTask = dispatcher.StartConsumingAsync(
            (_, _) =>
            {
                openBatchInvoked = true;
                return Task.CompletedTask;
            },
            cts.Token);

        // Cancel BEFORE enqueueing — the channel reader will throw OperationCanceledException
        // and the consumer exits without ever invoking openBatch.
        await cts.CancelAsync();
        dispatcher.Enqueue(new ActivationArgs([@"E:\Data\one.evtx"], []));

        await consumerTask;

        Assert.False(openBatchInvoked, "openBatch must not run after cancellation");
    }

    [Fact]
    public async Task ProcessBatch_RoutesFilesAsLogPathTypeFile_OnMainThread()
    {
        var mainThread = Substitute.For<IMainThreadService>();
        mainThread.InvokeOnMainThreadAsync(Arg.Any<Func<Task>>())
            .Returns(callInfo => callInfo.Arg<Func<Task>>().Invoke());

        var (dispatcher, channel) = CreateDispatcher(mainThread: mainThread);
        IEnumerable<(string Path, LogPathType Type)>? capturedPaths = null;
        bool? capturedCombine = null;

        var consumerTask = dispatcher.StartConsumingAsync(
            (paths, combine) =>
            {
                capturedPaths = paths;
                capturedCombine = combine;

                return Task.CompletedTask;
            },
            TestContext.Current.CancellationToken);

        dispatcher.Enqueue(new ActivationArgs([@"E:\Data\one.evtx", @"E:\Data\two.evtx"], []));

        channel.Writer.Complete();
        await consumerTask;

        Assert.NotNull(capturedPaths);
        var pathsList = capturedPaths.ToList();
        Assert.Equal(2, pathsList.Count);
        Assert.All(pathsList, p => Assert.Equal(LogPathType.File, p.Type));
        Assert.True(capturedCombine);
        await mainThread.Received().InvokeOnMainThreadAsync(Arg.Any<Func<Task>>());
    }

    [Fact]
    public async Task StartConsumingAsync_IsIdempotent_SecondCallReturnsImmediately()
    {
        var (dispatcher, channel) = CreateDispatcher();
        var firstInvocations = 0;
        var secondInvocations = 0;

        var firstTask = dispatcher.StartConsumingAsync(
            (_, _) => { Interlocked.Increment(ref firstInvocations); return Task.CompletedTask; },
            TestContext.Current.CancellationToken);

        await dispatcher.StartConsumingAsync(
            (_, _) => { Interlocked.Increment(ref secondInvocations); return Task.CompletedTask; },
            TestContext.Current.CancellationToken);

        dispatcher.Enqueue(new ActivationArgs([@"E:\sample.evtx"], []));

        Assert.Equal(0, secondInvocations);

        channel.Writer.Complete();
        await firstTask;
        Assert.Equal(1, firstInvocations);
    }

    [Fact]
    public async Task StartConsumingAsync_OnCancellation_ExitsGracefullyWithoutRethrow()
    {
        var (dispatcher, _) = CreateDispatcher();
        using var cts = new CancellationTokenSource();

        var consumerTask = dispatcher.StartConsumingAsync(
            (_, _) => Task.CompletedTask,
            cts.Token);

        await cts.CancelAsync();

        // No exception should escape — the dispatcher swallows OperationCanceledException on shutdown.
        await consumerTask;
    }

    [Fact]
    public async Task StartConsumingAsync_PerBatchExceptionIsIsolated_ConsumerKeepsGoing()
    {
        var logger = Substitute.For<ITraceLogger>();
        var (dispatcher, channel) = CreateDispatcher(logger: logger);
        var seenBatches = new List<int>();
        var batchIndex = 0;

        var consumerTask = dispatcher.StartConsumingAsync(
            (_, _) =>
            {
                var current = Interlocked.Increment(ref batchIndex);
                if (current == 1) { throw new InvalidOperationException("first batch fails"); }

                seenBatches.Add(current);

                return Task.CompletedTask;
            },
            TestContext.Current.CancellationToken);

        dispatcher.Enqueue(new ActivationArgs([@"E:\a.evtx"], []));
        dispatcher.Enqueue(new ActivationArgs([@"E:\b.evtx"], []));
        dispatcher.Enqueue(new ActivationArgs([@"E:\c.evtx"], []));

        channel.Writer.Complete();
        await consumerTask;

        Assert.Contains(2, seenBatches);
        Assert.Contains(3, seenBatches);
    }

    [Fact]
    public async Task StartConsumingAsync_RejectsNullOpenBatch()
    {
        var (dispatcher, _) = CreateDispatcher();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            dispatcher.StartConsumingAsync(null!, TestContext.Current.CancellationToken));
    }

    private static (ActivationDispatcher dispatcher, Channel<ActivationArgs> channel) CreateDispatcher(
        IAlertDialogService? dialogService = null,
        ITraceLogger? logger = null,
        IMainThreadService? mainThread = null)
    {
        var channel = Channel.CreateUnbounded<ActivationArgs>(new UnboundedChannelOptions { SingleReader = true });
        var resolvedMainThread = mainThread ?? Substitute.For<IMainThreadService>();

        if (mainThread is null)
        {
            // Default: run on-thread to keep tests deterministic.
            resolvedMainThread.InvokeOnMainThreadAsync(Arg.Any<Func<Task>>())
                .Returns(callInfo => callInfo.Arg<Func<Task>>().Invoke());
        }

        var dispatcher = new ActivationDispatcher(
            dialogService ?? Substitute.For<IAlertDialogService>(),
            logger ?? Substitute.For<ITraceLogger>(),
            resolvedMainThread,
            channel);

        return (dispatcher, channel);
    }
}
