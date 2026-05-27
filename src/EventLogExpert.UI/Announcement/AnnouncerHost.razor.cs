// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Announcement;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.UI.Announcement;

public sealed partial class AnnouncerHost : ComponentBase, IDisposable
{
    [Inject] private IAnnouncementService AnnouncementService { get; init; } = null!;

    public void Dispose() => AnnouncementService.StateChanged -= OnStateChanged;

    protected override void OnInitialized()
    {
        AnnouncementService.StateChanged += OnStateChanged;

        base.OnInitialized();
    }

    private void OnStateChanged() => _ = InvokeAsync(StateHasChanged);
}
