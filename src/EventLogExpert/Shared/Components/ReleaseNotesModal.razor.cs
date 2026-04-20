// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.UI.Services;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.Shared.Components;

public sealed partial class ReleaseNotesModal : IDisposable
{
    private string _html = string.Empty;

    [Inject] private ITraceLogger TraceLogger { get; init; } = null!;

    [Inject] private IUpdateService UpdateService { get; init; } = null!;

    public void Dispose() => UpdateService.ReleaseNotesReady -= OnReleaseNotesReady;

    protected override void OnInitialized()
    {
        UpdateService.ReleaseNotesReady += OnReleaseNotesReady;

        base.OnInitialized();
    }

    private async void OnReleaseNotesReady(ReleaseNotesContent content)
    {
        try
        {
            _html = ReleaseNotesMarkdownRenderer.RenderToHtml(content.Title, content.Markdown);

            await InvokeAsync(async () =>
            {
                StateHasChanged();
                await Open();
            });
        }
        catch (Exception ex)
        {
            TraceLogger.Error($"Failed to display release notes: {ex}");
        }
    }
}
