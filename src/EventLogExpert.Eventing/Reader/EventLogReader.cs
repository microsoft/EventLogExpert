// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Models;

namespace EventLogExpert.Eventing.Reader;

public sealed class EventLogReader : IDisposable
{
    private readonly EventLogHandle _handle;

    private IntPtr[] _buffer;
    private int _currentIndex;
    private bool _disposed;
    private int _total;

    public EventLogReader(string path, PathType pathType)
    {
        //_handle = EventMethods.EvtQuery(EventLogSession.GlobalSession.Handle, path, null, pathType == PathType.LogName ? 1 : 2);
    }

    // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
    // ~EventLogReader()
    // {
    //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
    //     Dispose(disposing: false);
    // }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (_disposed) { return; }

        if (disposing)
        {
            // TODO: dispose managed state (managed objects)
        }

        while (_currentIndex < _total)
        {
            EventMethods.EvtClose(_buffer[_currentIndex]);
            _currentIndex++;
        }

        _handle.Dispose();

        // TODO: free unmanaged resources (unmanaged objects) and override finalizer
        // TODO: set large fields to null
        _disposed = true;
    }
}
