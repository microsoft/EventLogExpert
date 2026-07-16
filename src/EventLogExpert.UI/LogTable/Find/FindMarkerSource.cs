// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;

namespace EventLogExpert.UI.LogTable.Find;

/// <inheritdoc cref="IFindMarkerSource" />
public sealed class FindMarkerSource : IFindMarkerSource
{
    private IReadOnlyList<long> _ticks = [];

    public event EventHandler? MarksChanged;

    public EventLogId? Owner { get; private set; }

    public IReadOnlyList<long> Ticks => _ticks;

    public void Clear()
    {
        if (Owner is null && _ticks.Count == 0) { return; }

        Owner = null;
        _ticks = [];
        MarksChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Publish(EventLogId owner, IReadOnlyList<long> sortedTicks)
    {
        ArgumentNullException.ThrowIfNull(sortedTicks);

        Owner = owner;
        _ticks = [.. sortedTicks];
        MarksChanged?.Invoke(this, EventArgs.Empty);
    }
}
