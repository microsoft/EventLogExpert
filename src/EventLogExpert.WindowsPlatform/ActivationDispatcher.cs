// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.Common.Activation;
using EventLogExpert.Runtime.Common.Threading;
using System.Threading.Channels;

namespace EventLogExpert.WindowsPlatform;

/// <summary>
///     Owns a single-reader <see cref="Channel{T}" /> that serializes activation requests so concurrent right-clicks
///     never interleave open-batch calls. Folder expansion happens off the UI thread; the batch open is marshaled back to
///     the UI thread via <see cref="IMainThreadService.InvokeOnMainThreadAsync(Func{Task})" />.
/// </summary>
public sealed class ActivationDispatcher : IActivationDispatcher
{
    private readonly Channel<ActivationArgs> _channel;
    private readonly IAlertDialogService _dialogService;
    private readonly ITraceLogger _logger;
    private readonly IMainThreadService _mainThread;

    private int _started;

    public ActivationDispatcher(IAlertDialogService dialogService, ITraceLogger logger, IMainThreadService mainThread)
        : this(dialogService, logger, mainThread, channel: null) { }

    internal ActivationDispatcher(
        IAlertDialogService dialogService,
        ITraceLogger logger,
        IMainThreadService mainThread,
        Channel<ActivationArgs>? channel)
    {
        ArgumentNullException.ThrowIfNull(dialogService);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(mainThread);

        _dialogService = dialogService;
        _logger = logger;
        _mainThread = mainThread;
        _channel = channel ?? Channel.CreateUnbounded<ActivationArgs>(
            new UnboundedChannelOptions { SingleReader = true });
    }

    public void Enqueue(ActivationArgs args)
    {
        if (args.IsEmpty) { return; }

        if (!_channel.Writer.TryWrite(args))
        {
            _logger.Warning($"Activation args dropped: channel write rejected (writer completed). Files={args.FilePaths.Count}, Folders={args.FolderPaths.Count}");
        }
    }

    public async Task StartConsumingAsync(
        Func<IEnumerable<(string Path, LogPathType Type)>, bool, Task> openBatch,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(openBatch);

        // Guard against duplicate subscriptions — MainPage subscribes from two places.
        if (Interlocked.Exchange(ref _started, 1) == 1) { return; }

        try
        {
            await foreach (var args in _channel.Reader.ReadAllAsync(cancellationToken))
            {
                try
                {
                    await ProcessBatchAsync(args, openBatch, cancellationToken);
                }
                // Cancellation during a batch is a clean shutdown, not a dispatch error — re-throw
                // so the outer await-foreach loop exits via its own OperationCanceledException catch.
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.Error($"Activation dispatch failed: {ex}");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static (List<(string Path, LogPathType Type)> Paths, List<(string Title, string Message)> Alerts) ExpandToOpenList(ActivationArgs args)
    {
        var paths = new List<(string, LogPathType)>(args.FilePaths.Count + (args.FolderPaths.Count * 4));
        var alerts = new List<(string, string)>();

        foreach (var file in args.FilePaths)
        {
            paths.Add((file, LogPathType.File));
        }

        foreach (var folder in args.FolderPaths)
        {
            var result = EvtxFolderEnumerator.EnumerateEvtxTopLevel(folder);

            if (result is EvtxEnumerationResult.Success success)
            {
                paths.AddRange(success.Files.Select(f => (f, LogPathType.File)));
            }

            var alertCopy = EvtxFolderEnumerator.ToAlertCopy(result);

            if (alertCopy is { } copy)
            {
                alerts.Add(copy);
            }
        }

        return (paths, alerts);
    }

    private async Task ProcessBatchAsync(
        ActivationArgs args,
        Func<IEnumerable<(string Path, LogPathType Type)>, bool, Task> openBatch,
        CancellationToken cancellationToken)
    {
        // Pass the token to Task.Run so cancellation BEFORE the work starts short-circuits without
        // running ExpandToOpenList (which does filesystem I/O for folder paths).
        var (paths, alerts) = await Task.Run(() => ExpandToOpenList(args), cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        foreach (var (title, message) in alerts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _mainThread.InvokeOnMainThreadAsync(() =>
                _dialogService.ShowAlert(title, message, "Ok"));
        }

        if (paths.Count == 0) { return; }

        cancellationToken.ThrowIfCancellationRequested();

        const bool CombineLog = true;
        await _mainThread.InvokeOnMainThreadAsync(() => openBatch(paths, CombineLog));
    }
}
