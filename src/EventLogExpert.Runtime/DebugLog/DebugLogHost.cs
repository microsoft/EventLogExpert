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
///     log-level setting to the routing baseline, and persists unhandled exceptions to the file sink. This is an eager
///     lifecycle singleton - nothing depends on it, so the composition root MUST force-resolve it at startup (see the
///     eager <c>GetRequiredService</c> call in the MAUI head). It depends on <see cref="FileLogSink" /> so the DI
///     container disposes it BEFORE the sink, detaching both hooks before the writer closes.
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
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
    }

    public void Dispose()
    {
        if (_disposed) { return; }

        _disposed = true;

        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
        _settings.LogLevelChanged -= OnLogLevelChanged;
    }

    private void OnLogLevelChanged() => _routingPolicy.UpdateGlobalBaseline(_settings.LogLevel);

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e) =>
        _fileSink.EmitUnfiltered(new LogRecord(
            DateTime.UtcNow,
            LogLevel.Critical,
            $"Unhandled Exception: {e.ExceptionObject}",
            string.Empty));
}
