// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Histogram;

public enum HistogramDimension
{
    Severity,
    Source,
    EventId,
    TaskCategory,
    Opcode,
    Log,
    LogonType,
    TicketEncryptionType,
    ErrorCode,
    ProcessImage,
    ParentProcessImage
}
