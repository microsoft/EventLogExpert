// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace EventLogExpert.Runtime.FilterLibrary;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "Kind")]
[JsonDerivedType(typeof(LibraryEntrySavedFilter), "Filter")]
[JsonDerivedType(typeof(LibraryEntryPreset), "Preset")]
public abstract record LibraryEntry
{
    public LibraryEntryId Id { get; init; } = LibraryEntryId.Create();

    public required string Name { get; init; }

    public required DateTimeOffset CreatedUtc { get; init; }

    public bool IsFavorite { get; internal init; }

    public DateTimeOffset? LastUsedUtc { get; internal init; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    internal LibraryEntryOrigin Origin { get; init; } = LibraryEntryOrigin.UserSaved;
}
