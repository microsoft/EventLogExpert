// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;

namespace EventLogExpert.Runtime.LogTable;

public readonly record struct DisplayRow(EventLocator Loc, ResolvedEvent Lean);
