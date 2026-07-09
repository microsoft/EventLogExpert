// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Common.Activation;
using Microsoft.Windows.AppLifecycle;
using System.Collections.Concurrent;

namespace EventLogExpert.Platforms.Windows.Activation;

/// <summary>
///     Bridges <c>Program.Main</c> (captures the cold-launch activation args before DI is built) and the
///     <see cref="IActivationDispatcher" /> singleton (constructed by MAUI DI). Seeded args are buffered until
///     <see cref="AttachDispatcher" /> runs on the DI construction thread and drains them, so args delivered before the UI
///     consumer exists are not lost.
/// </summary>
internal static class ActivationBootstrap
{
    private static readonly Lock s_attachLock = new();
    private static readonly ConcurrentQueue<ActivationArgs> s_buffer = new();

    private static IActivationDispatcher? s_dispatcher;

    internal static void AttachDispatcher(IActivationDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);

        lock (s_attachLock)
        {
            s_dispatcher = dispatcher;

            while (s_buffer.TryDequeue(out var args))
            {
                dispatcher.Enqueue(args);
            }
        }
    }

    internal static void SeedColdLaunch(AppActivationArguments? coldLaunch)
    {
        EnqueueArgs(ActivationArgsExtractor.Extract(coldLaunch));
    }

    private static void EnqueueArgs(ActivationArgs args)
    {
        if (args.IsEmpty) { return; }

        lock (s_attachLock)
        {
            if (s_dispatcher is not null)
            {
                s_dispatcher.Enqueue(args);

                return;
            }

            s_buffer.Enqueue(args);
        }
    }
}
