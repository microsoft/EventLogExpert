// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Text.Json;

namespace EventLogExpert.Runtime.FilterLibrary;

public abstract record ImportValidationError
{
    private protected ImportValidationError() { }

    public sealed record EmptyFile : ImportValidationError;

    public sealed record EntryIdExpectedJsonString(JsonTokenType ActualTokenType) : ImportValidationError;

    public sealed record EntryIdExpectedNonEmptyString : ImportValidationError;

    public sealed record EntryIdInvalidGuid(string Raw) : ImportValidationError;

    public sealed record InvalidBasicFilters(IReadOnlyList<string> EntryNames) : ImportValidationError
    {
        public bool Equals(InvalidBasicFilters? other) =>
            other is not null && EntryNames.SequenceEqual(other.EntryNames, StringComparer.Ordinal);

        public override int GetHashCode()
        {
            var hash = new HashCode();

            foreach (var entryName in EntryNames)
            {
                hash.Add(entryName, StringComparer.Ordinal);
            }

            return hash.ToHashCode();
        }
    }

    public sealed record InvalidSchemaVersion(int Version) : ImportValidationError;

    public sealed record MissingEntriesProperty : ImportValidationError;

    public sealed record MissingEntryName : ImportValidationError;

    public sealed record NativeDetail(string Detail) : ImportValidationError;

    public sealed record NormalizedBasicFilterFormatFailed(string ComparisonText) : ImportValidationError;

    public sealed record NormalizedBasicFilterRebuildFailed(string ComparisonText) : ImportValidationError;

    public sealed record SchemaVersionNotInteger : ImportValidationError;

    public sealed record TagsExpectedArrayOrNull(JsonTokenType ActualTokenType) : ImportValidationError;

    public sealed record TagsExpectedStringElement(JsonTokenType ActualTokenType) : ImportValidationError;

    public sealed record TagsUnexpectedEnd : ImportValidationError;

    public sealed record UnknownLibraryEntryKind(string Kind) : ImportValidationError;

    public sealed record UnsupportedSchemaVersion(int Version) : ImportValidationError;

    public sealed record UnsupportedShape : ImportValidationError;
}
