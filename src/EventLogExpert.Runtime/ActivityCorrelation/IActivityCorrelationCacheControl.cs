// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.ActivityCorrelation;

/// <summary>
///     Lets the close pipeline drop the single-slot correlation view cache so a closed log's neighborhood is not
///     retained. A build that completes after a close is separately kept from repopulating the cache by the content-token
///     recheck in <c>BuildAsync</c>.
/// </summary>
internal interface IActivityCorrelationCacheControl
{
    void Invalidate();
}
