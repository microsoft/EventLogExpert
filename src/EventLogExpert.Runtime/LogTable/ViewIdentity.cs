// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Filtering.Evaluation;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.LogTable;

internal sealed class ViewIdentity : IEquatable<ViewIdentity>
{
    private readonly EventLogId? _activeLogId;
    private readonly Filter _filter;
    private readonly ColumnName? _groupBy;
    private readonly int _hash;
    private readonly bool _isDescending;
    private readonly bool _isGroupDescending;

    private readonly bool _isMultiLogDisplay;
    private readonly ColumnName? _orderBy;

    private readonly ImmutableArray<EventLogId> _scope;
    private readonly bool _timelineVisible;

    internal ViewIdentity(
        EventLogId? activeLogId,
        ImmutableArray<EventLogId> scope,
        ColumnName? orderBy,
        bool isDescending,
        ColumnName? groupBy,
        bool isGroupDescending,
        bool timelineVisible,
        bool isMultiLogDisplay,
        Filter filter)
    {
        _activeLogId = activeLogId;
        _scope = scope;
        _orderBy = orderBy;
        _isDescending = isDescending;
        _groupBy = groupBy;
        _isGroupDescending = isGroupDescending;
        _timelineVisible = timelineVisible;
        _isMultiLogDisplay = isMultiLogDisplay;
        _filter = filter;
        _hash = ComputeHash();
    }

    internal EventLogId? ActiveLogId => _activeLogId;

    internal Filter Filter => _filter;

    internal bool IsMultiLogDisplay => _isMultiLogDisplay;

    internal ColumnName? RequestedGroupBy => _groupBy;

    internal bool RequestedIsDescending => _isDescending;

    internal bool RequestedIsGroupDescending => _isGroupDescending;

    internal ColumnName? RequestedOrderBy => _orderBy;

    internal ImmutableArray<EventLogId> Scope => _scope;

    internal bool TimelineVisible => _timelineVisible;

    public static bool operator ==(ViewIdentity? left, ViewIdentity? right) =>
        left?.Equals(right) ?? right is null;

    public static bool operator !=(ViewIdentity? left, ViewIdentity? right) => !(left == right);

    public bool Equals(ViewIdentity? other)
    {
        if (ReferenceEquals(this, other)) { return true; }

        if (other is null || _hash != other._hash) { return false; }

        return _activeLogId == other._activeLogId && CoversSameViewAs(other);
    }

    public override bool Equals(object? obj) => Equals(obj as ViewIdentity);

    public override int GetHashCode() => _hash;

    internal bool CoversSameViewAs(ViewIdentity other)
    {
        if (_orderBy != other._orderBy ||
            _isDescending != other._isDescending ||
            _groupBy != other._groupBy ||
            _isGroupDescending != other._isGroupDescending ||
            _timelineVisible != other._timelineVisible ||
            _isMultiLogDisplay != other._isMultiLogDisplay)
        {
            return false;
        }

        if (_scope.Length != other._scope.Length) { return false; }

        for (int index = 0; index < _scope.Length; index++)
        {
            if (_scope[index] != other._scope[index]) { return false; }
        }

        return !_filter.HasFilteringChangedFrom(other._filter);
    }

    private int ComputeHash()
    {
        var hash = new HashCode();

        hash.Add(_activeLogId);
        hash.Add(_orderBy);
        hash.Add(_isDescending);
        hash.Add(_groupBy);
        hash.Add(_isGroupDescending);
        hash.Add(_timelineVisible);
        hash.Add(_isMultiLogDisplay);

        foreach (EventLogId logId in _scope) { hash.Add(logId); }

        hash.Add(_filter.DateFilter);

        ImmutableArray<FilterSnapshot> snapshots = _filter.Snapshots;

        if (snapshots.IsDefault)
        {
            hash.Add(-1);
        }
        else
        {
            hash.Add(snapshots.Length);

            foreach (FilterSnapshot snapshot in snapshots) { hash.Add(snapshot); }
        }

        return hash.ToHashCode();
    }
}
