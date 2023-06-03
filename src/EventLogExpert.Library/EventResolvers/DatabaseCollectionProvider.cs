// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.Helpers;
using System.Collections.Immutable;

namespace EventLogExpert.Library.EventResolvers;

public class DatabaseCollectionProvider : IDatabaseCollectionProvider
{
    private readonly ITraceLogger _logger;

    public DatabaseCollectionProvider(ITraceLogger traceLogger)
    {
        _logger = traceLogger;
    }

    public ImmutableList<string> ActiveDatabases { get; private set; } = ImmutableList<string>.Empty;

    public void SetActiveDatabases(IEnumerable<string> activeDatabases)
    {
        _logger.Trace($"{nameof(SetActiveDatabases)} was called with {activeDatabases.Count()} databases.");
        ActiveDatabases = activeDatabases.ToImmutableList();
    }
}
