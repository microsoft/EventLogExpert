// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Common.Sources;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.EventLog;

public interface ILoadedLogNamesSource : IChangeNotifier
{
    ImmutableHashSet<string> Current { get; }
}
