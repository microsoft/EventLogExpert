// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.Helpers;

namespace EventLogExpert.Library.Models;

public record DisplayEventModel(
    long? RecordId,
    DateTime? TimeCreated,
    int Id,
    string MachineName,
    SeverityLevel? Level,
    string ProviderName,
    string TaskDisplayName,
    string Description,
    string Xml
);
