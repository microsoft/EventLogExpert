// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Common.Elevation;

/// <summary>Outcome of a request to relaunch the current process with administrator privileges.</summary>
public enum ElevationResult
{
    /// <summary>The UAC prompt was accepted and the elevated process was started; the original instance should exit.</summary>
    Relaunched,

    /// <summary>The user declined the UAC prompt; the original instance remains running.</summary>
    UserCancelled,

    /// <summary>An unexpected error prevented the relaunch attempt; the original instance remains running.</summary>
    Failed
}
