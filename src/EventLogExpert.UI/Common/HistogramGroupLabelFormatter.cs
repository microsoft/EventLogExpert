// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Localization;
using EventLogExpert.Runtime.Histogram;
using Microsoft.Extensions.Localization;

namespace EventLogExpert.UI.Common;

internal static class HistogramGroupLabelFormatter
{
    internal static string Format(IStringLocalizer<SharedResource> localizer, HistogramGroupLabel label) =>
        label switch
        {
            HistogramGroupLabel.SeverityBucket severity => FormatSeverity(localizer, severity.Bucket),
            HistogramGroupLabel.CategoricalOther other => FormatCategoricalOther(localizer,
                other.Dimension,
                other.FoldedCount),
            HistogramGroupLabel.DataValue dataValue => dataValue.Text,
            _ => throw new ArgumentOutOfRangeException(nameof(label), label, null)
        };

    private static string FormatCategoricalOther(
        IStringLocalizer<SharedResource> localizer,
        HistogramDimension dimension,
        int foldedCount) =>
        dimension switch
        {
            HistogramDimension.EventId => foldedCount == 0 ? localizer["Histogram_Overflow_Bare"] :
                localizer[foldedCount == 1 ? "Histogram_Overflow_EventId_One" : "Histogram_Overflow_EventId_Many",
                    foldedCount],
            HistogramDimension.Source => foldedCount == 0 ? localizer["Histogram_Overflow_Bare"] :
                localizer[foldedCount == 1 ? "Histogram_Overflow_Source_One" : "Histogram_Overflow_Source_Many",
                    foldedCount],
            HistogramDimension.TaskCategory => foldedCount == 0 ? localizer["Histogram_Overflow_Bare"] :
                localizer[foldedCount == 1 ? "Histogram_Overflow_TaskCategory_One" :
                        "Histogram_Overflow_TaskCategory_Many",
                    foldedCount],
            HistogramDimension.Opcode => foldedCount == 0 ? localizer["Histogram_Overflow_Bare"] :
                localizer[foldedCount == 1 ? "Histogram_Overflow_Opcode_One" : "Histogram_Overflow_Opcode_Many",
                    foldedCount],
            HistogramDimension.Log => foldedCount == 0 ? localizer["Histogram_Overflow_Bare"] :
                localizer[foldedCount == 1 ? "Histogram_Overflow_Log_One" : "Histogram_Overflow_Log_Many", foldedCount],
            HistogramDimension.LogonType => foldedCount == 0 ? localizer["Histogram_Overflow_Bare"] :
                localizer[foldedCount == 1 ? "Histogram_Overflow_LogonType_One" : "Histogram_Overflow_LogonType_Many",
                    foldedCount],
            HistogramDimension.TicketEncryptionType => foldedCount == 0 ? localizer["Histogram_Overflow_Bare"] :
                localizer[foldedCount == 1 ? "Histogram_Overflow_TicketEncryptionType_One" :
                        "Histogram_Overflow_TicketEncryptionType_Many",
                    foldedCount],
            HistogramDimension.ErrorCode => foldedCount == 0 ? localizer["Histogram_Overflow_Bare"] :
                localizer[foldedCount == 1 ? "Histogram_Overflow_ErrorCode_One" : "Histogram_Overflow_ErrorCode_Many",
                    foldedCount],
            HistogramDimension.ProcessImage => foldedCount == 0 ? localizer["Histogram_Overflow_Bare"] :
                localizer[foldedCount == 1 ? "Histogram_Overflow_ProcessImage_One" :
                        "Histogram_Overflow_ProcessImage_Many",
                    foldedCount],
            HistogramDimension.ParentProcessImage => foldedCount == 0 ? localizer["Histogram_Overflow_Bare"] :
                localizer[foldedCount == 1 ? "Histogram_Overflow_ParentProcessImage_One" :
                        "Histogram_Overflow_ParentProcessImage_Many",
                    foldedCount],
            _ => throw new ArgumentOutOfRangeException(nameof(dimension), dimension, null)
        };

    private static string FormatSeverity(IStringLocalizer<SharedResource> localizer, HistogramSeverityBucket bucket) =>
        bucket switch
        {
            HistogramSeverityBucket.Errors => localizer["Histogram_Severity_Errors"],
            HistogramSeverityBucket.Warnings => localizer["Histogram_Severity_Warnings"],
            HistogramSeverityBucket.Other => localizer["Histogram_Severity_Other"],
            _ => throw new ArgumentOutOfRangeException(nameof(bucket), bucket, null)
        };
}
