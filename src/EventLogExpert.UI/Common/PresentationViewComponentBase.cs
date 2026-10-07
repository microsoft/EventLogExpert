// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.LogTable;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.UI.Common;

public abstract class PresentationViewComponentBase : AppStateComponentBase
{
    private bool _disposed;

    private int _hasPendingRenderDispatch;

    protected OrderedViewPresentation Presentation { get; private set; } = null!;

    [Inject]
    protected IOrderedViewSource ViewSource { get; init; } = null!;

    protected override async ValueTask DisposeAsyncCore(bool disposing)
    {
        if (disposing)
        {
            _disposed = true;
            ViewSource.Updated -= OnViewUpdated;
        }

        await base.DisposeAsyncCore(disposing);
    }

    protected override void OnInitialized()
    {
        ViewSource.Updated += OnViewUpdated;
        Presentation = ViewSource.Current;

        base.OnInitialized();
    }

    protected virtual void OnPresentationChanged() { }

    /// <remarks>
    ///     Runs on the publishing thread for every publication, before render coalescing, so an override can observe a
    ///     transient state (e.g. a reorder-pending view) the coalesced paint may skip. It MUST be thread-safe and MUST NOT
    ///     render or read <see cref="Presentation" /> - use <see cref="OnPresentationChanged" /> for that.
    /// </remarks>
    protected virtual void OnPresentationPublished(OrderedViewPresentation presentation) { }

    private void AdoptLatestPresentation()
    {
        Volatile.Write(ref _hasPendingRenderDispatch, 0);
        Presentation = ViewSource.Current;
        OnPresentationChanged();
        StateHasChanged();
    }

    private async Task DispatchRenderAsync()
    {
        if (_disposed) { return; }

        try { await InvokeAsync(AdoptLatestPresentation); }
        catch (ObjectDisposedException) { }
        catch (OperationCanceledException) { }
    }

    private void OnViewUpdated(OrderedViewPresentation presentation)
    {
        OnPresentationPublished(presentation);

        if (Interlocked.Exchange(ref _hasPendingRenderDispatch, 1) != 0) { return; }

        _ = DispatchRenderAsync();
    }
}
