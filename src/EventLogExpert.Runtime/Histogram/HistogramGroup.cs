// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Histogram;

public sealed record HistogramGroup(HistogramGroupLabel Label, string ColorClass, string Key, int[] SlotIndices);
