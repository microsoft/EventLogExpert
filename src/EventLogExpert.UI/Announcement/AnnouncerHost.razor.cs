// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Localization;
using EventLogExpert.Runtime.Announcement;
using EventLogExpert.UI.Common;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using AnnouncementPayload = EventLogExpert.Runtime.Announcement.Announcement;

namespace EventLogExpert.UI.Announcement;

public sealed partial class AnnouncerHost : ComponentBase, IDisposable
{
    [Inject] private IAnnouncementService AnnouncementService { get; init; } = null!;

    [Inject] private IStringLocalizer<SharedResource> Localizer { get; init; } = null!;

    public void Dispose() => AnnouncementService.StateChanged -= OnStateChanged;

    protected override void OnInitialized()
    {
        AnnouncementService.StateChanged += OnStateChanged;

        base.OnInitialized();
    }

    private void OnStateChanged() => _ = InvokeAsync(StateHasChanged);

    private string RenderedAnnouncement()
    {
        var current = AnnouncementService.Current;

        var text = current.Payload switch
        {
            AnnouncementPayload.Text(var message) => message,
            AnnouncementPayload.LensKept(var label) =>
                Localizer["FilterLens_KeptAnnouncement", FilterLensLabelFormatter.Format(Localizer, label)].Value,
            _ => throw new ArgumentOutOfRangeException(nameof(current.Payload), current.Payload, null)
        };

        // The re-announce toggle lives here (moved from the service) so it is applied AFTER localization: an odd seq
        // appends a zero-width space so two identical consecutive announcements still mutate the rendered text node.
        // SR live regions do not re-announce when the text is unchanged; NVDA/JAWS/VoiceOver do not pronounce the ZWSP.
        return current.Sequence % 2 == 0 ? text : text + "\u200B";
    }
}
