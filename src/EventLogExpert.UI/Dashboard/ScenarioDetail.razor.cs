// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Filtering.Basic;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Runtime.Scenarios;
using EventLogExpert.Scenarios.Catalog;
using EventLogExpert.UI.Common;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.UI.Dashboard;

public sealed partial class ScenarioDetail
{
    private readonly string _nameId = ComponentId.NewUnique().Value;
    private readonly string _offlineId = ComponentId.NewUnique().Value;

    [Parameter] public bool CanEnableChannels { get; set; }

    [Parameter] public IReadOnlyList<ChannelReadiness> ChannelReadiness { get; set; } = [];

    [Parameter] public bool IsBusy { get; set; }

    [Parameter] public bool IsDisabled { get; set; }

    [Parameter] public bool IsFavored { get; set; }

    [Parameter] public bool IsLivePresent { get; set; } = true;

    [Parameter] public EventCallback<string> OnEnableChannel { get; set; }

    [Parameter] public EventCallback OnLaunch { get; set; }

    [Parameter] public EventCallback OnLaunchFromFolder { get; set; }

    [Parameter] public EventCallback OnToggleFavorite { get; set; }

    [Parameter] public IReadOnlyList<ChannelReadiness> OptionalChannelReadiness { get; set; } = [];

    [Parameter][EditorRequired] public ScenarioDefinition Scenario { get; set; } = null!;

    private IReadOnlyList<ChannelReadiness> DisplayOptionalReadiness =>
        OptionalChannelReadiness.Count > 0 ? OptionalChannelReadiness :
        Scenario.OptionalChannels.IsDefaultOrEmpty ? [] :
        [
            .. Scenario.OptionalChannels.Select(channel =>
                new ChannelReadiness(channel, ChannelPresence.Unknown, ChannelEnablement.Unknown))
        ];

    private IReadOnlyList<ChannelReadiness> DisplayReadiness =>
        ChannelReadiness.Count > 0 ? ChannelReadiness :
        [
            .. Scenario.Channels.Select(channel =>
                new ChannelReadiness(channel, ChannelPresence.Unknown, ChannelEnablement.Unknown))
        ];

    private IReadOnlyList<FilterLine> FilterLines
    {
        get
        {
            if (Scenario.Filters.IsDefaultOrEmpty) { return []; }

            List<FilterLine> lines = [];

            foreach (var row in Scenario.Filters)
            {
                if (!BasicFilterFormatter.TryFormat(row.Filter, out var text)) { continue; }

                lines.Add(new FilterLine(row.IsExcluded ? $"Exclude {text}" : text, row.Color));
            }

            return lines;
        }
    }

    private static string EnablementLabel(ChannelEnablement enablement) => enablement switch
    {
        ChannelEnablement.Enabled => "Enabled",
        ChannelEnablement.Disabled => "Disabled",
        _ => "Enablement unknown"
    };

    private static bool IsSystemChannel(string channel) =>
        string.Equals(channel, LogChannelNames.ApplicationLog, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(channel, LogChannelNames.SystemLog, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(channel, LogChannelNames.SecurityLog, StringComparison.OrdinalIgnoreCase);

    private static string PresenceLabel(ChannelPresence presence) => presence switch
    {
        ChannelPresence.Present => "Present",
        ChannelPresence.Absent => "Not present",
        _ => "Presence unknown"
    };

    // A disabled required channel can be enabled in place only when it is actually present and is not one of the classic
    // system logs (Application/System/Security), which Windows does not allow toggling.
    private bool CanOfferEnable(ChannelReadiness readiness) =>
        readiness.Presence == ChannelPresence.Present &&
        readiness.Enablement == ChannelEnablement.Disabled &&
        !IsSystemChannel(readiness.Channel);

    private async Task LaunchAsync()
    {
        if (IsDisabled) { return; }

        await OnLaunch.InvokeAsync();
    }

    private async Task LaunchFromFolderAsync()
    {
        if (IsBusy) { return; }

        await OnLaunchFromFolder.InvokeAsync();
    }

    private readonly record struct FilterLine(string Text, HighlightColor Color);
}
