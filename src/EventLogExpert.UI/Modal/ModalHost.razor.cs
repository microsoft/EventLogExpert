// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Modal;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.UI.Modal;

public sealed partial class ModalHost : ComponentBase, IDisposable
{
    [Inject] private IModalService Service { get; init; } = null!;

    public void Dispose() => Service.StateChanged -= OnStateChanged;

    protected override void OnInitialized()
    {
        Service.StateChanged += OnStateChanged;
        base.OnInitialized();
    }

    private void OnStateChanged() => _ = InvokeAsync(StateHasChanged);
}
