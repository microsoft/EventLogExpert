// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace EventLogExpert.Runtime.FilterLibrary;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "Kind")]
[JsonDerivedType(typeof(LibraryEntrySavedFilter), "Filter")]
[JsonDerivedType(typeof(LibraryEntryPreset), "Preset")]
public abstract record LibraryEntry(string Id, string Name, DateTimeOffset CreatedUtc);
