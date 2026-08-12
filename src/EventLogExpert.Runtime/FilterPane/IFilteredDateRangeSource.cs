// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Evaluation;
using EventLogExpert.Runtime.Common.Sources;

namespace EventLogExpert.Runtime.FilterPane;

public interface IFilteredDateRangeSource : IChangeNotifier
{
    DateFilter? Current { get; }
}
