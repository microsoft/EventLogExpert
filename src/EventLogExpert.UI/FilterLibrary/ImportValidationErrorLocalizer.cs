// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Localization;
using EventLogExpert.Runtime.FilterLibrary;
using EventLogExpert.UI.Common;
using Microsoft.Extensions.Localization;

namespace EventLogExpert.UI.FilterLibrary;

internal static class ImportValidationErrorLocalizer
{
    internal static string Describe(IStringLocalizer<SharedResource> localizer, ImportValidationError error) => error switch
    {
        ImportValidationError.EmptyFile => localizer["FilterImport_Error_EmptyFile"],
        ImportValidationError.SchemaVersionNotInteger => localizer["FilterImport_Error_SchemaVersionNotInteger"],
        ImportValidationError.UnsupportedSchemaVersion unsupported =>
            localizer["FilterImport_Error_UnsupportedSchemaVersion", unsupported.Version],
        ImportValidationError.InvalidSchemaVersion invalid =>
            localizer["FilterImport_Error_InvalidSchemaVersion", invalid.Version],
        ImportValidationError.MissingEntriesProperty => localizer["FilterImport_Error_MissingEntriesProperty"],
        ImportValidationError.UnsupportedShape => localizer["FilterImport_Error_UnsupportedShape"],
        ImportValidationError.MissingEntryName => localizer["FilterImport_Error_MissingEntryName"],
        ImportValidationError.InvalidBasicFilters invalidBasicFilters =>
            LocalizedCount.OneOrManyRaw(
                localizer,
                invalidBasicFilters.EntryNames.Count,
                "FilterImport_Error_InvalidBasicFilters_One",
                "FilterImport_Error_InvalidBasicFilters_Many",
                string.Join(", ", invalidBasicFilters.EntryNames)),
        ImportValidationError.EntryIdExpectedJsonString expectedJsonString =>
            localizer["FilterImport_Error_EntryIdExpectedJsonString", expectedJsonString.ActualTokenType],
        ImportValidationError.EntryIdExpectedNonEmptyString => localizer["FilterImport_Error_EntryIdExpectedNonEmptyString"],
        ImportValidationError.EntryIdInvalidGuid invalidGuid =>
            localizer["FilterImport_Error_EntryIdInvalidGuid", invalidGuid.Raw],
        ImportValidationError.TagsExpectedArrayOrNull expectedArray =>
            localizer["FilterImport_Error_TagsExpectedArrayOrNull", expectedArray.ActualTokenType],
        ImportValidationError.TagsExpectedStringElement expectedElement =>
            localizer["FilterImport_Error_TagsExpectedStringElement", expectedElement.ActualTokenType],
        ImportValidationError.TagsUnexpectedEnd => localizer["FilterImport_Error_TagsUnexpectedEnd"],
        ImportValidationError.UnknownLibraryEntryKind unknownKind =>
            localizer["FilterImport_Error_UnknownLibraryEntryKind", unknownKind.Kind],
        ImportValidationError.NormalizedBasicFilterFormatFailed formatFailed =>
            localizer["FilterImport_Error_NormalizedBasicFilterFormatFailed", formatFailed.ComparisonText],
        ImportValidationError.NormalizedBasicFilterRebuildFailed rebuildFailed =>
            localizer["FilterImport_Error_NormalizedBasicFilterRebuildFailed", rebuildFailed.ComparisonText],
        ImportValidationError.NativeDetail nativeDetail => nativeDetail.Detail,
        _ => throw new ArgumentOutOfRangeException(nameof(error), error.GetType(), null)
    };
}
