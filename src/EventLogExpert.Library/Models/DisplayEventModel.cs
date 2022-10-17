namespace EventLogExpert.Library.Models;

public record DisplayEventModel(
    long? RecordId,
    DateTime? TimeCreated,
    int Id,
    string MachineName,
    string LevelDisplayName,
    string ProviderName,
    string TaskDisplayName,
    string Description
);