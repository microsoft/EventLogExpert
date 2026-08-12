// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Common.Sources;

namespace EventLogExpert.Runtime.Histogram;

public interface IHistogramDimensionRequestSource : IChangeNotifier
{
    HistogramDimensionRequest? Current { get; }
}
