// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Histogram;

public sealed record HistogramDimensionRequest(HistogramDimension Dimension, long Token);
