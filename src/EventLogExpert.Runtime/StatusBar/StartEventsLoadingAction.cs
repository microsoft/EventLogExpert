// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.StatusBar;

/// <summary>
///     Registers a new event log loading activity. Progress for it is reported with
///     <see cref="SetEventsLoadingAction" />.
/// </summary>
/// <param name="ActivityId">A unique id that distinguishes this loading activity from others.</param>
/// <param name="LoadGeneration">
///     The <see cref="StatusBarState.LoadGeneration" /> the load observed when it was still
///     valid. A close-all advances the generation, so a stale start from a cancelled load is ignored.
/// </param>
public sealed record StartEventsLoadingAction(StatusActivityId ActivityId, int LoadGeneration);
