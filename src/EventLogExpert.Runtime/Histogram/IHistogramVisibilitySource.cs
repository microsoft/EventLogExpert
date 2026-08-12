// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Common.Sources;

namespace EventLogExpert.Runtime.Histogram;

public interface IHistogramVisibilitySource : IChangeNotifier
{
    bool IsVisible { get; }
}
