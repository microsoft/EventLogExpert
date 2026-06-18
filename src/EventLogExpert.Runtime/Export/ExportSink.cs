// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Export;

internal abstract class ExportSink(int columnCount) : IAsyncDisposable
{
    private bool _completed;
    private bool _disposed;
    private bool _headerWritten;

    public async ValueTask CompleteAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_headerWritten)
        {
            throw new InvalidOperationException("The header row must be written before completing the export.");
        }

        if (_completed)
        {
            throw new InvalidOperationException("The export has already been completed.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        await CompleteCoreAsync(cancellationToken).ConfigureAwait(false);
        _completed = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await DisposeCoreAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    public async ValueTask WriteHeaderAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_headerWritten)
        {
            throw new InvalidOperationException("The header row has already been written.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        await WriteHeaderCoreAsync(cancellationToken).ConfigureAwait(false);
        _headerWritten = true;
    }

    public async ValueTask WriteRowAsync(IReadOnlyList<string?> cells, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cells);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_headerWritten)
        {
            throw new InvalidOperationException("The header row must be written before any data row.");
        }

        if (_completed)
        {
            throw new InvalidOperationException("Cannot write a row after the export has been completed.");
        }

        if (cells.Count != columnCount)
        {
            throw new ArgumentException(
                $"The row has {cells.Count} cells but {columnCount} columns were declared.", nameof(cells));
        }

        cancellationToken.ThrowIfCancellationRequested();

        await WriteRowCoreAsync(cells, cancellationToken).ConfigureAwait(false);
    }

    protected abstract ValueTask CompleteCoreAsync(CancellationToken cancellationToken);

    protected abstract ValueTask DisposeCoreAsync();

    protected abstract ValueTask WriteHeaderCoreAsync(CancellationToken cancellationToken);

    protected abstract ValueTask WriteRowCoreAsync(IReadOnlyList<string?> cells, CancellationToken cancellationToken);
}
