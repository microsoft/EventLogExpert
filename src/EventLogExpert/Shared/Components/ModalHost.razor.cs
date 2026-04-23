// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Services;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.Shared.Components;

public sealed partial class ModalHost : ComponentBase, IDisposable
{
    [Inject] private IModalService Service { get; init; } = null!;

    public void Dispose() => Service.StateChanged -= OnStateChanged;

    protected override void OnInitialized()
    {
        Service.StateChanged += OnStateChanged;
        base.OnInitialized();
    }

    private void OnStateChanged() => InvokeAsync(StateHasChanged);
}
