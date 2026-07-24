// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Scenarios;

/// <summary>
///     A progress phase reported by <see cref="IScenarioLaunchService.LaunchFromFolderAsync" /> so a caller can
///     surface a cancellable busy indicator only while cancellation is meaningful.
/// </summary>
public enum ScenarioFolderPhase
{
    /// <summary>The folder picker has returned a folder and the cancellable enumeration/probe scan is starting.</summary>
    Scanning,

    /// <summary>
    ///     The scan matched logs and is committing to open them. Past this point the operation is no longer cancellable,
    ///     so a caller should stop offering Cancel.
    /// </summary>
    Opening
}
