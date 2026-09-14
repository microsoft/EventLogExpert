// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Localization;
using EventLogExpert.Runtime.FilterLibrary;
using EventLogExpert.UI.FilterLibrary;
using EventLogExpert.UI.Tests.Localization;
using EventLogExpert.UI.Tests.TestUtils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;

namespace EventLogExpert.UI.Tests.FilterLibrary;

public sealed class ImportValidationErrorLocalizerTests
{
    private readonly MarkerLocalizer _localizer = new();

    public static TheoryData<ImportValidationError, string> KeyedErrors() => new()
    {
        { new ImportValidationError.EmptyFile(), "[[FilterImport_Error_EmptyFile]]" },
        { new ImportValidationError.SchemaVersionNotInteger(), "[[FilterImport_Error_SchemaVersionNotInteger]]" },
        { new ImportValidationError.UnsupportedSchemaVersion(2), "[[FilterImport_Error_UnsupportedSchemaVersion(2)]]" },
        { new ImportValidationError.InvalidSchemaVersion(0), "[[FilterImport_Error_InvalidSchemaVersion(0)]]" },
        { new ImportValidationError.MissingEntriesProperty(), "[[FilterImport_Error_MissingEntriesProperty]]" },
        { new ImportValidationError.UnsupportedShape(), "[[FilterImport_Error_UnsupportedShape]]" },
        { new ImportValidationError.MissingEntryName(), "[[FilterImport_Error_MissingEntryName]]" },
        { new ImportValidationError.InvalidBasicFilters(["One"]), "[[FilterImport_Error_InvalidBasicFilters_One(One)]]" },
        { new ImportValidationError.InvalidBasicFilters(["One", "Two"]), "[[FilterImport_Error_InvalidBasicFilters_Many(One, Two)]]" },
        { new ImportValidationError.EntryIdExpectedJsonString(JsonTokenType.Number), "[[FilterImport_Error_EntryIdExpectedJsonString(Number)]]" },
        { new ImportValidationError.EntryIdExpectedNonEmptyString(), "[[FilterImport_Error_EntryIdExpectedNonEmptyString]]" },
        { new ImportValidationError.EntryIdInvalidGuid("bad"), "[[FilterImport_Error_EntryIdInvalidGuid(bad)]]" },
        { new ImportValidationError.TagsExpectedArrayOrNull(JsonTokenType.Number), "[[FilterImport_Error_TagsExpectedArrayOrNull(Number)]]" },
        { new ImportValidationError.TagsExpectedStringElement(JsonTokenType.Number), "[[FilterImport_Error_TagsExpectedStringElement(Number)]]" },
        { new ImportValidationError.TagsUnexpectedEnd(), "[[FilterImport_Error_TagsUnexpectedEnd]]" },
        { new ImportValidationError.UnknownLibraryEntryKind("Unknown"), "[[FilterImport_Error_UnknownLibraryEntryKind(Unknown)]]" },
        { new ImportValidationError.NormalizedBasicFilterFormatFailed("Level == 4"), "[[FilterImport_Error_NormalizedBasicFilterFormatFailed(Level == 4)]]" },
        { new ImportValidationError.NormalizedBasicFilterRebuildFailed("Level == 4"), "[[FilterImport_Error_NormalizedBasicFilterRebuildFailed(Level == 4)]]" }
    };

    [Fact]
    public void Describe_HandlesEveryConcreteErrorLeaf()
    {
        foreach (var leafType in typeof(ImportValidationError).GetNestedTypes().Where(type => !type.IsAbstract))
        {
            var error = CreateError(leafType);
            var actual = ImportValidationErrorLocalizer.Describe(_localizer, error);

            if (leafType == typeof(ImportValidationError.NativeDetail))
            {
                Assert.Equal("native detail", actual);
            }
            else
            {
                Assert.StartsWith($"[[FilterImport_Error_{leafType.Name}", actual, StringComparison.Ordinal);
                Assert.EndsWith("]]", actual, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void Describe_NativeDetail_PassesDetailThroughVerbatim() =>
        Assert.Equal("native detail", ImportValidationErrorLocalizer.Describe(_localizer, new ImportValidationError.NativeDetail("native detail")));

    [Theory]
    [MemberData(nameof(KeyedErrors))]
    public void Describe_RoutesEveryKeyedErrorToExpectedKey(ImportValidationError error, string expected) =>
        Assert.Equal(expected, ImportValidationErrorLocalizer.Describe(_localizer, error));

    [Fact]
    public void NeutralInvalidBasicFilterSingular_UsesApprovedGrammarCorrection()
    {
        var actual = WithEnUsCulture(() => ImportValidationErrorLocalizer.Describe(
            BuildLocalizer(),
            new ImportValidationError.InvalidBasicFilters(["Broken"])));

        Assert.Equal(
            "Import file contains a Basic filter that did not parse into a valid Basic filter: Broken. Remove or fix it and re-import.",
            actual);
    }

    [Fact]
    public void NeutralValues_HaveExpectedManifestValues()
    {
        Assert.Equal("Expected JSON string for LibraryEntryId, got {0}.", NeutralValue("FilterImport_Error_EntryIdExpectedJsonString"));
        Assert.Equal("Unknown LibraryEntry kind: {0}", NeutralValue("FilterImport_Error_UnknownLibraryEntryKind"));
    }

    private static IStringLocalizer<SharedResource> BuildLocalizer() =>
        new ServiceCollection()
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .AddEventLogLocalization()
            .BuildServiceProvider()
            .GetRequiredService<IStringLocalizer<SharedResource>>();

    private static ImportValidationError CreateError(Type leafType) =>
        leafType == typeof(ImportValidationError.EmptyFile) ? new ImportValidationError.EmptyFile() :
        leafType == typeof(ImportValidationError.EntryIdExpectedJsonString) ? new ImportValidationError.EntryIdExpectedJsonString(JsonTokenType.Number) :
        leafType == typeof(ImportValidationError.EntryIdExpectedNonEmptyString) ? new ImportValidationError.EntryIdExpectedNonEmptyString() :
        leafType == typeof(ImportValidationError.EntryIdInvalidGuid) ? new ImportValidationError.EntryIdInvalidGuid("bad") :
        leafType == typeof(ImportValidationError.InvalidBasicFilters) ? new ImportValidationError.InvalidBasicFilters(["One"]) :
        leafType == typeof(ImportValidationError.InvalidSchemaVersion) ? new ImportValidationError.InvalidSchemaVersion(0) :
        leafType == typeof(ImportValidationError.MissingEntriesProperty) ? new ImportValidationError.MissingEntriesProperty() :
        leafType == typeof(ImportValidationError.MissingEntryName) ? new ImportValidationError.MissingEntryName() :
        leafType == typeof(ImportValidationError.NativeDetail) ? new ImportValidationError.NativeDetail("native detail") :
        leafType == typeof(ImportValidationError.NormalizedBasicFilterFormatFailed) ? new ImportValidationError.NormalizedBasicFilterFormatFailed("Level == 4") :
        leafType == typeof(ImportValidationError.NormalizedBasicFilterRebuildFailed) ? new ImportValidationError.NormalizedBasicFilterRebuildFailed("Level == 4") :
        leafType == typeof(ImportValidationError.SchemaVersionNotInteger) ? new ImportValidationError.SchemaVersionNotInteger() :
        leafType == typeof(ImportValidationError.TagsExpectedArrayOrNull) ? new ImportValidationError.TagsExpectedArrayOrNull(JsonTokenType.Number) :
        leafType == typeof(ImportValidationError.TagsExpectedStringElement) ? new ImportValidationError.TagsExpectedStringElement(JsonTokenType.Number) :
        leafType == typeof(ImportValidationError.TagsUnexpectedEnd) ? new ImportValidationError.TagsUnexpectedEnd() :
        leafType == typeof(ImportValidationError.UnknownLibraryEntryKind) ? new ImportValidationError.UnknownLibraryEntryKind("Unknown") :
        leafType == typeof(ImportValidationError.UnsupportedSchemaVersion) ? new ImportValidationError.UnsupportedSchemaVersion(2) :
        leafType == typeof(ImportValidationError.UnsupportedShape) ? new ImportValidationError.UnsupportedShape() :
        throw new InvalidOperationException($"No test fixture for {leafType.FullName}.");

    private static string NeutralValue(string key) =>
        XDocument.Load(LocalizationSourceScan.ResxPath)
            .Root!
            .Elements("data")
            .Single(element => (string?)element.Attribute("name") == key)
            .Element("value")!
            .Value;

    private static T WithEnUsCulture<T>(Func<T> action)
    {
        CultureInfo priorCulture = CultureInfo.CurrentCulture;
        CultureInfo priorUiCulture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");

            return action();
        }
        finally
        {
            CultureInfo.CurrentCulture = priorCulture;
            CultureInfo.CurrentUICulture = priorUiCulture;
        }
    }
}
