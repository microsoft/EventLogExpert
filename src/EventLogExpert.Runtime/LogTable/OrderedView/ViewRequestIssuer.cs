// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.LogTable.OrderedView;

internal sealed class ViewRequestIssuer
{
    private readonly Lock _gate = new();

    private volatile bool _enabled = true;
    private Exception? _lastFault;
    private ViewIdentity? _lastIssuedIdentity;

    private ViewIdentity? _recoveringIdentity;
    private long _recoveringWatermark;

    private long _sequence;

    public bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    public Exception? LastFault => Volatile.Read(ref _lastFault);

    public void ForceReissue()
    {
        lock (_gate)
        {
            _lastIssuedIdentity = null;
        }
    }

    public void RecordFault(Exception fault) => Volatile.Write(ref _lastFault, fault);

    public long ResetForClear()
    {
        lock (_gate)
        {
            _lastIssuedIdentity = null;

            return ++_sequence;
        }
    }

    public long ResetForCloseAll()
    {
        lock (_gate)
        {
            _lastIssuedIdentity = null;
            _recoveringIdentity = null;
            _recoveringWatermark = 0;

            return ++_sequence;
        }
    }

    public bool TryBeginRecovery(ViewIdentity identity, long servedWatermark)
    {
        lock (_gate)
        {
            if (_recoveringIdentity == identity && servedWatermark <= _recoveringWatermark) { return false; }

            _recoveringIdentity = identity;
            _recoveringWatermark = servedWatermark;

            return true;
        }
    }

    public long? TryIssue(ViewIdentity identity)
    {
        lock (_gate)
        {
            if (_lastIssuedIdentity == identity) { return null; }

            _lastIssuedIdentity = identity;

            return ++_sequence;
        }
    }
}
