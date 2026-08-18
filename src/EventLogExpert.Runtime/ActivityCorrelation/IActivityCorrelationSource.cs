// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Common.Sources;

namespace EventLogExpert.Runtime.ActivityCorrelation;

/// <summary>
///     Notifies when the raw event store changes, so a correlation consumer can re-check whether a built view has
///     gone stale (via <see cref="IActivityCorrelationService.TryGetContentToken" />) and offer a refresh.
/// </summary>
public interface IActivityCorrelationSource : IChangeNotifier;
