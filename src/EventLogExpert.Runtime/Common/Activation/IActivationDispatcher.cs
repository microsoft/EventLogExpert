// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;

namespace EventLogExpert.Runtime.Common.Activation;

/// <summary>
///     Buffers Windows shell activation requests (cold-launch, command-line, folder right-click) so first-activation
///     args delivered before the UI consumer attaches are not lost. <see cref="StartConsumingAsync" /> is idempotent
///     (guarded by <see cref="Interlocked" />) — safe to call from both <c>MainPage</c>'s constructor and
///     <c>BlazorWebViewInitialized</c> so a missing WebView2 runtime does not strand buffered args.
/// </summary>
public interface IActivationDispatcher
{
    void Enqueue(ActivationArgs args);

    Task StartConsumingAsync(
        Func<IEnumerable<(string Path, LogPathType Type)>, bool, Task> openBatch,
        CancellationToken cancellationToken);
}
