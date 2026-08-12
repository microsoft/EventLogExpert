// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Common.Sources;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.EventLog;

public interface IEventSelectionSource : IChangeNotifier
{
    ImmutableList<SelectionEntry> Current { get; }
}
