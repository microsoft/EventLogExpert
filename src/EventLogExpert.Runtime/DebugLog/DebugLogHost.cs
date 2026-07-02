// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Logging.Routing;
using EventLogExpert.Logging.Sinks;
using EventLogExpert.Runtime.Settings;
using Microsoft.Extensions.Logging;

namespace EventLogExpert.Runtime.DebugLog;

/// <summary>
///     Owns the application-level logging hooks that previously lived in the file sink: it bridges the user's global
///     log-level setting to the routing baseline and the verbose-resolution toggle to a runtime routing override, and
///     persists unhandled exceptions to the file sink. This is an eager lifecycle singleton - nothing depends on it, so
///     the composition root MUST force-resolve it at startup (see the eager <c>GetRequiredService</c> call in the MAUI
///     head) or runtime toggling is silently dropped; the INITIAL verbose state is seeded at
///     <see cref="LogRoutingPolicy" /> construction so a persisted-ON toggle survives even before this resolve. It depends
///     on <see cref="FileLogSink" /> so the DI container disposes it BEFORE the sink, detaching the hooks before the
///     writer closes.
/// </summary>
public sealed class DebugLogHost : IDisposable
{
    private readonly FileLogSink _fileSink;
    private readonly LogRoutingPolicy _routingPolicy;
    private readonly ISettingsService _settings;

    private bool _disposed;

    public DebugLogHost(FileLogSink fileSink, LogRoutingPolicy routingPolicy, ISettingsService settings)
    {
        _fileSink = fileSink;
        _routingPolicy = routingPolicy;
        _settings = settings;

        _settings.LogLevelChanged += OnLogLevelChanged;
        _settings.VerboseResolutionChanged += OnVerboseResolutionChanged;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
    }

    public void Dispose()
    {
        if (_disposed) { return; }

        _disposed = true;

        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
        _settings.LogLevelChanged -= OnLogLevelChanged;
        _settings.VerboseResolutionChanged -= OnVerboseResolutionChanged;
    }

    private void OnLogLevelChanged() => _routingPolicy.UpdateGlobalBaseline(_settings.LogLevel);

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e) =>
        _fileSink.EmitUnfiltered(new LogRecord(
            DateTime.UtcNow,
            LogLevel.Critical,
            $"Unhandled Exception: {e.ExceptionObject}",
            string.Empty));

    // Raises (or resets) the whole Resolution.* subtree to Trace on demand so a user can troubleshoot why an event
    // failed to resolve without dropping the global baseline. The initial state is seeded at LogRoutingPolicy
    // construction; this bridge handles subsequent toggles.
    private void OnVerboseResolutionChanged() => _routingPolicy.SetCategoryOverride(
        LogCategories.Resolution,
        _settings.VerboseResolution ? LogLevel.Trace : null);
}
