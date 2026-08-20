// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;

namespace EventLogExpert.Eventing.Resolvers;

internal readonly record struct ResolvedDescription(string Text, EventResolutionStatus Status);
