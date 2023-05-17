// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.Helpers;

namespace EventLogExpert.Library.Models;

public record DisplayEventModel(
    long? RecordId,
    DateTime? TimeCreated,
    int Id,
    string ComputerName,
    SeverityLevel? Level,
    string Source,
    string TaskCategory,
    string Description,
    string Xml
);
