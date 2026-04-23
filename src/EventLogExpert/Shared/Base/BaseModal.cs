// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Services;
using Fluxor.Blazor.Web.Components;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.Shared.Base;

/// <summary>
/// Code-only abstract base for modals shown via <see cref="IModalService"/>. Owns the per-show
/// identifier handshake with the service and provides Complete/Cancel helpers that close the
/// dialog (via <see cref="ChromeRef"/>) before completing the awaiting task.
/// </summary>
/// <remarks>
/// Concrete modals should:
/// <list type="bullet">
/// <item>Inherit <see cref="BaseModal{TResult}"/>.</item>
/// <item>Render a <see cref="ModalChrome"/> with <c>@ref="ChromeRef"</c>.</item>
/// <item>Wire the chrome's footer callbacks to the virtual <c>OnXxxAsync</c> methods on this base.</item>
/// <item>Wire <see cref="ModalChrome.OnDialogClosedByUser"/> to <see cref="HandleDialogClosedByUserAsync"/>.</item>
/// </list>
/// </remarks>
public abstract class BaseModal<TResult> : FluxorComponent
{
    private long _modalId;

    [Inject] internal IModalService ModalService { get; init; } = null!;

    /// <summary>Bound by concrete modals via <c>@ref</c> on their <see cref="ModalChrome"/>.</summary>
    protected ModalChrome? ChromeRef { get; set; }

    /// <summary>Public close helper for child components (e.g., a row inside a list) to dismiss
    /// the host modal. Equivalent to completing with <c>default(TResult)</c>.</summary>
    public Task CloseAsync() => CompleteAsync(default);

    /// <summary>Wired to <see cref="ModalChrome.OnDialogClosedByUser"/>. Runs cleanup then completes
    /// with default. The dialog itself stays open until the host re-renders without this modal
    /// (DynamicComponent disposal triggers ChromeRef teardown which closes the &lt;dialog&gt;).</summary>
    internal async Task HandleDialogClosedByUserAsync()
    {
        await OnClosingAsync();
        ModalService.Complete(_modalId, default(TResult));
    }

    /// <summary>Close the dialog and complete the awaiting task with <paramref name="result"/>.</summary>
    protected async Task CompleteAsync(TResult? result)
    {
        await OnClosingAsync();

        if (ChromeRef is not null)
        {
            await ChromeRef.CloseAsync();
        }

        ModalService.Complete(_modalId, result);
    }

    protected override async ValueTask DisposeAsyncCore(bool disposing)
    {
        if (disposing)
        {
            // Defensive: ensure the awaiting task is completed even if we were torn down without
            // an explicit close path running. Stale ids are ignored by the service so this is a
            // no-op when Complete already ran.
            ModalService.Complete(_modalId, default(TResult));
        }

        await base.DisposeAsyncCore(disposing);
    }

    /// <summary>"Accept" hook used by Dismiss / AcceptCancel presets (alerts, confirms).</summary>
    protected virtual Task OnAcceptAsync() => CompleteAsync(default);

    /// <summary>Footer "Cancel" / "Exit" / "Close" hook. Default = complete with <c>default(TResult)</c>.</summary>
    protected virtual Task OnCancelAsync() => CompleteAsync(default);

    /// <summary>Cleanup hook invoked from <see cref="CompleteAsync"/> and the user-initiated close
    /// path. Override to run side-effects that must happen regardless of how the modal closes.</summary>
    protected virtual Task OnClosingAsync() => Task.CompletedTask;

    /// <summary>Footer "Export" hook. Default = no-op (override in modals that use ImportExportClose).</summary>
    protected virtual Task OnExportAsync() => Task.CompletedTask;

    /// <summary>Footer "Import" hook. Default = no-op (override in modals that use ImportExportClose).</summary>
    protected virtual Task OnImportAsync() => Task.CompletedTask;

    protected override void OnInitialized()
    {
        // Capture the id assigned by the service when this modal became active; subsequent
        // Complete calls use this id so a stale modal cannot complete the wrong task.
        _modalId = ModalService.ActiveModalId;
        base.OnInitialized();
    }

    /// <summary>Footer "Save" hook. Default = complete with <c>default(TResult)</c>; override for save logic.</summary>
    protected virtual Task OnSaveAsync() => CompleteAsync(default);
}

