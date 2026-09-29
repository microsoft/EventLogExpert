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
                PluralText.Format(localizer, "Histogram_Overflow_EventId", ("count", foldedCount)),
            HistogramDimension.Source => foldedCount == 0 ? localizer["Histogram_Overflow_Bare"] :
                PluralText.Format(localizer, "Histogram_Overflow_Source", ("count", foldedCount)),
            HistogramDimension.TaskCategory => foldedCount == 0 ? localizer["Histogram_Overflow_Bare"] :
                PluralText.Format(localizer, "Histogram_Overflow_TaskCategory", ("count", foldedCount)),
            HistogramDimension.Opcode => foldedCount == 0 ? localizer["Histogram_Overflow_Bare"] :
                PluralText.Format(localizer, "Histogram_Overflow_Opcode", ("count", foldedCount)),
            HistogramDimension.Log => foldedCount == 0 ? localizer["Histogram_Overflow_Bare"] :
                PluralText.Format(localizer, "Histogram_Overflow_Log", ("count", foldedCount)),
            HistogramDimension.LogonType => foldedCount == 0 ? localizer["Histogram_Overflow_Bare"] :
                PluralText.Format(localizer, "Histogram_Overflow_LogonType", ("count", foldedCount)),
            HistogramDimension.TicketEncryptionType => foldedCount == 0 ? localizer["Histogram_Overflow_Bare"] :
                PluralText.Format(localizer, "Histogram_Overflow_TicketEncryptionType", ("count", foldedCount)),
            HistogramDimension.ErrorCode => foldedCount == 0 ? localizer["Histogram_Overflow_Bare"] :
                PluralText.Format(localizer, "Histogram_Overflow_ErrorCode", ("count", foldedCount)),
            HistogramDimension.ProcessImage => foldedCount == 0 ? localizer["Histogram_Overflow_Bare"] :
                PluralText.Format(localizer, "Histogram_Overflow_ProcessImage", ("count", foldedCount)),
            HistogramDimension.ParentProcessImage => foldedCount == 0 ? localizer["Histogram_Overflow_Bare"] :
                PluralText.Format(localizer, "Histogram_Overflow_ParentProcessImage", ("count", foldedCount)),
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
