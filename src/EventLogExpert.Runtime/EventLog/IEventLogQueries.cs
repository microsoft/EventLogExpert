// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Common.Filtering;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.EventLog;

public interface IEventLogQueries
{
    IReadOnlyList<string> GetChannelNames();

    ImmutableArray<string> GetEventDataFieldNames();

    ImmutableArray<string> GetEventDataFieldValues(string fieldName);

    (DateTime After, DateTime Before) GetEventDateRange(DateTime fallbackUtcNow);

    ImmutableArray<string> GetPropertyValues(EventProperty property);

    ImmutableArray<string> GetUserDataFieldNames();

    ImmutableArray<string> GetUserDataFieldValues(string fieldName);

    bool IsContinuouslyUpdating();
}
