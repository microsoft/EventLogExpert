// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.LogTable.OrderedView;

internal sealed class ViewRequestIssuer
{
    private readonly Lock _gate = new();

    private bool _clearFaultPending;
    private volatile bool _enabled = true;
    private bool _forcePending;
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

    public void ArmForce()
    {
        lock (_gate)
        {
            _forcePending = true;
        }
    }

    public void ArmRecovery()
    {
        lock (_gate)
        {
            _forcePending = true;
            _clearFaultPending = true;
        }
    }

    public void RecordFault(Exception fault) => Volatile.Write(ref _lastFault, fault);

    public long ResetForClear()
    {
        lock (_gate)
        {
            _lastIssuedIdentity = null;
            _forcePending = false;
            _clearFaultPending = false;

            return ++_sequence;
        }
    }

    public long ResetForCloseAll()
    {
        lock (_gate)
        {
            _lastIssuedIdentity = null;
            _forcePending = false;
            _clearFaultPending = false;
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

    public IssueOutcome TryIssue(ViewIdentity expectedIdentity, Func<ViewProbe> probe)
    {
        lock (_gate)
        {
            if (!_forcePending && _lastIssuedIdentity == expectedIdentity) { return IssueOutcome.Deduped; }

            ViewProbe probed = probe();

            if (probed.LiveIdentity != expectedIdentity) { return IssueOutcome.Stale; }

            bool clearFault = _clearFaultPending;

            _lastIssuedIdentity = expectedIdentity;
            _forcePending = false;
            _clearFaultPending = false;

            return IssueOutcome.Issued(++_sequence, probed, clearFault);
        }
    }
}
