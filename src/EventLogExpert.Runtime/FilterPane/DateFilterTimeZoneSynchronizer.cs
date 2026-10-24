// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Settings;
using Fluxor;

namespace EventLogExpert.Runtime.FilterPane;

/// <summary>
///     Re-anchors the applied date range in a new display zone; must be resolved at startup before any component
///     subscribes to <see cref="ISettingsService.TimeZoneChanged" />.
/// </summary>
public sealed class DateFilterTimeZoneSynchronizer : IDisposable
{
    private readonly IDispatcher _dispatcher;
    private readonly ISettingsService _settings;

    private TimeZoneInfo _currentZone;
    private bool _disposed;

    public DateFilterTimeZoneSynchronizer(ISettingsService settings, IDispatcher dispatcher)
    {
        _settings = settings;
        _dispatcher = dispatcher;
        _currentZone = settings.TimeZoneInfo;

        _settings.TimeZoneChanged += OnTimeZoneChanged;
    }

    public void Dispose()
    {
        if (_disposed) { return; }

        _disposed = true;

        _settings.TimeZoneChanged -= OnTimeZoneChanged;
    }

    private void OnTimeZoneChanged(object? sender, TimeZoneInfo newZone)
    {
        var previousZone = _currentZone;
        _currentZone = newZone;

        if (previousZone.Equals(newZone)) { return; }

        _dispatcher.Dispatch(new ReinterpretFilterDateRangeAction(previousZone, newZone));
    }
}
