// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Localization;
using EventLogExpert.Runtime.Histogram;
using Microsoft.Extensions.Localization;

namespace EventLogExpert.UI.Common;

internal static class HistogramDimensionLocalizer
{
    internal static string Label(IStringLocalizer<SharedResource> localizer, HistogramDimension dimension) => dimension switch
    {
        HistogramDimension.Severity => localizer["Histogram_Dimension_Severity"],
        HistogramDimension.Source => localizer["Histogram_Dimension_Source"],
        HistogramDimension.EventId => localizer["Histogram_Dimension_EventId"],
        HistogramDimension.TaskCategory => localizer["Histogram_Dimension_TaskCategory"],
        HistogramDimension.Opcode => localizer["Histogram_Dimension_Opcode"],
        HistogramDimension.Log => localizer["Histogram_Dimension_Log"],
        HistogramDimension.LogonType => localizer["Histogram_Dimension_LogonType"],
        HistogramDimension.TicketEncryptionType => localizer["Histogram_Dimension_TicketEncryptionType"],
        HistogramDimension.ErrorCode => localizer["Histogram_Dimension_ErrorCode"],
        HistogramDimension.ProcessImage => localizer["Histogram_Dimension_ProcessImage"],
        HistogramDimension.ParentProcessImage => localizer["Histogram_Dimension_ParentProcessImage"],
        _ => throw new ArgumentOutOfRangeException(nameof(dimension), dimension, null)
    };
}
