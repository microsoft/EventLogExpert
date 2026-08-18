// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.ActivityCorrelation;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.FilterLenses;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.Settings;
using EventLogExpert.UI.Common;
using Microsoft.AspNetCore.Components;
using System.Globalization;

namespace EventLogExpert.UI.DetailsPane;

public sealed partial class ActivityCorrelationPanel : AppStateComponentBase
{
    private const int SnippetMaxLength = 120;
    private readonly Dictionary<EventLocator, EventDisplay> _eventCache = [];

    private readonly HashSet<Guid> _expanded = [];

    private CancellationTokenSource? _buildCts;
    private bool _building;
    private EventLocator? _builtHandle;
    private Guid _lastFocusActivity;
    private bool _stale;
    private ActivityCorrelationView? _view;

    [Parameter] public EventLocator? FocusedHandle { get; set; }

    [Parameter] public bool IsActive { get; set; }

    [Parameter] public ResolvedEvent? SelectedEvent { get; set; }

    [Inject] private IActivityCorrelationService CorrelationService { get; init; } = null!;

    [Inject] private IActivityCorrelationSource CorrelationSource { get; init; } = null!;

    [Inject] private IEventDetailResolver DetailResolver { get; init; } = null!;

    [Inject] private IEventLogCommands EventLogCommands { get; init; } = null!;

    [Inject] private IFilterLensCommands FilterLensCommands { get; init; } = null!;

    [Inject] private ISettingsService Settings { get; init; } = null!;

    [Inject] private ITraceLogger TraceLogger { get; init; } = null!;

    protected override ValueTask DisposeAsyncCore(bool disposing)
    {
        if (disposing) { CancelBuild(); }

        return base.DisposeAsyncCore(disposing);
    }

    protected override void OnInitialized()
    {
        ObserveSource(CorrelationSource, OnStoreChangedAsync);

        base.OnInitialized();
    }

    protected override async Task OnParametersSetAsync()
    {
        // Lazy: only build while the Correlation tab is active. On deactivation cancel any in-flight build; if one was
        // actually running, drop the built handle so re-activation rebuilds. A completed view is retained for instant reuse.
        if (!IsActive)
        {
            CancelBuild();

            if (_building)
            {
                _builtHandle = null;
                _building = false;
            }

            return;
        }

        if (SelectedEvent?.ActivityId is not { } activity || activity == Guid.Empty || FocusedHandle is not { } handle)
        {
            CancelBuild();
            _view = null;
            _building = false;
            _stale = false;
            _builtHandle = null;

            return;
        }

        if (handle == _builtHandle) { return; }

        await BuildAsync(handle);
    }

    private static string BuildSnippet(string? description)
    {
        if (string.IsNullOrEmpty(description)) { return string.Empty; }

        int i = 0;
        int length = description.Length;

        // Return the first non-empty logical line, trimmed and length-capped; keeps the DOM bounded and skips blank lines.
        while (i < length)
        {
            int lineStart = i;

            while (i < length && description[i] != '\n' && description[i] != '\r') { i++; }

            ReadOnlySpan<char> line = description.AsSpan(lineStart, i - lineStart).Trim();

            if (line.Length > 0)
            {
                return line.Length > SnippetMaxLength ? string.Concat(line[..SnippetMaxLength], "\u2026") : line.ToString();
            }

            while (i < length && (description[i] == '\n' || description[i] == '\r')) { i++; }
        }

        return string.Empty;
    }

    private static string FormatActivityId(Guid activityId)
    {
        string text = activityId.ToString();

        return text.Length > 8 ? text[..8] : text;
    }

    private static string FormatEventCount(int count) => $"{count} event{(count == 1 ? "" : "s")}";

    private static string RoleLabel(ActivityNodeRole role) =>
        role switch
        {
            ActivityNodeRole.Focus => "This activity",
            ActivityNodeRole.Parent => "Parent",
            ActivityNodeRole.Child => "Child",
            _ => string.Empty
        };

    private static string SeverityIconClass(SeverityLevel? severity) => SeverityIcon.CssClass(severity);

    private async Task BuildAsync(EventLocator handle)
    {
        CancelBuild();

        var cts = new CancellationTokenSource();
        _buildCts = cts;
        _builtHandle = handle;
        _building = true;
        _stale = false;
        _view = null;
        StateHasChanged();

        ActivityCorrelationView? view;

        try
        {
            view = await CorrelationService.BuildAsync(handle, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // A newer selection superseded this build; that build owns the panel state.
            return;
        }
        catch (Exception ex)
        {
            TraceLogger.Error($"{nameof(ActivityCorrelationPanel)}: correlation build failed: {ex}");

            if (!IsDisposed && ReferenceEquals(_buildCts, cts))
            {
                _building = false;
                _view = null;
                _builtHandle = null;
                StateHasChanged();
            }

            return;
        }

        if (IsDisposed || cts.IsCancellationRequested || !ReferenceEquals(_buildCts, cts)) { return; }

        _view = view;
        _building = false;

        // A null result (log not yet in the store, or a superseded locator) is not a durable build: drop the built
        // handle so re-activation or a re-selection retries instead of sticking on the unavailable state.
        if (view is null) { _builtHandle = null; }

        // If the store changed while this build ran, the freshly built view is already stale: offer Refresh and block
        // navigation immediately rather than presenting content the live store has moved past.
        _stale = IsStale();
        _eventCache.Clear();
        SeedExpansion(view);
        StateHasChanged();
    }

    private void CancelBuild()
    {
        if (_buildCts is { } cts)
        {
            try { cts.Cancel(); } catch (ObjectDisposedException) { /* Already disposed; cancel is moot. */ }

            cts.Dispose();
            _buildCts = null;
        }
    }

    private string FormatSpan(ActivityNode node)
    {
        if (node.EventCount == 0) { return "not in this log"; }

        string start = FormatTime(node.MinTicks);

        return node.MinTicks == node.MaxTicks ? start : $"{start} to {FormatTime(node.MaxTicks)}";
    }

    private string FormatTime(long ticks) =>
        TimeZoneInfo.ConvertTimeFromUtc(new DateTime(ticks, DateTimeKind.Utc), Settings.TimeZoneInfo)
            .ToString("g", CultureInfo.CurrentCulture);

    private bool IsExpanded(ActivityNode node) => _expanded.Contains(node.ActivityId);

    private bool IsSelected(CorrelatedEvent correlatedEvent) => FocusedHandle == correlatedEvent.Locator;

    private bool IsStale() =>
        _view is { } view && (!CorrelationService.TryGetContentToken(view.LogId, out var token) || token != view.Token);

    private void OnEventActivated(CorrelatedEvent correlatedEvent)
    {
        // Navigation is disabled while the correlation view is stale (its locators may address replaced content); Refresh rebuilds it.
        // Re-validate the snapshot synchronously too, closing the window between a live-tail change and its async notification.
        if (_stale || IsStale())
        {
            _stale = true;
            StateHasChanged();

            return;
        }

        var entry = new SelectionEntry(correlatedEvent.Locator, correlatedEvent.Locator, null);
        EventLogCommands.SetSelectedEvents([entry], entry);

        // One-shot reveal: scrolls the table if the row is in the current view, otherwise the consumer discards it - so a
        // filtered-out event still selects (details resolve from raw storage) without leaving a pending reveal.
        EventLogCommands.RequestRevealFocus(correlatedEvent.Locator, waitForView: false);
    }

    private void OnFilterToActivity(Guid activityId) =>
        FilterLensCommands.ShowRelatedByActivityId(activityId, SelectedEvent?.OwningLog);

    private Task OnStoreChangedAsync()
    {
        if (!_stale && IsStale())
        {
            _stale = true;
            StateHasChanged();
        }

        return Task.CompletedTask;
    }

    private async Task RefreshAsync()
    {
        if (FocusedHandle is { } handle) { await BuildAsync(handle); }
    }

    private EventDisplay ResolveEvent(CorrelatedEvent correlatedEvent)
    {
        if (_eventCache.TryGetValue(correlatedEvent.Locator, out var cached)) { return cached; }

        // Only timezone-independent fields are cached; the row time is formatted at render from TimeTicks so a timezone
        // change is reflected without invalidating this cache.
        EventDisplay display = DetailResolver.TryResolveLean(correlatedEvent.Locator, out var detail) ?
            new EventDisplay(detail.Id,
                detail.Source,
                BuildSnippet(detail.Description),
                LevelSeverity.FromLevelName(detail.Level)) : new EventDisplay(null, string.Empty, string.Empty, null);

        _eventCache[correlatedEvent.Locator] = display;

        return display;
    }

    private void SeedExpansion(ActivityCorrelationView? view)
    {
        if (view is null)
        {
            _expanded.Clear();
            _lastFocusActivity = Guid.Empty;

            return;
        }

        // The focus activity's timeline is always shown; only the secondary parent/child chips use expand state. On a new
        // root focus, reset chip expansions; on a same-focus Refresh, keep expansions for activities still present.
        if (view.FocusActivityId != _lastFocusActivity)
        {
            _lastFocusActivity = view.FocusActivityId;
            _expanded.Clear();

            return;
        }

        _expanded.RemoveWhere(activityId => view.Activities.All(node => node.ActivityId != activityId));
    }

    private void ToggleExpand(Guid activityId)
    {
        if (!_expanded.Add(activityId)) { _expanded.Remove(activityId); }
    }

    private readonly record struct EventDisplay(int? EventId, string Source, string MessageSnippet, SeverityLevel? Severity);
}
