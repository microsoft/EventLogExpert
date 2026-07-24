// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Eventing.Resolvers;
using EventLogExpert.Eventing.TestUtils;

namespace EventLogExpert.Eventing.Tests.Resolvers;

// Windows synthesizes "%1".."%N" <data> names for classic positional insertion strings (e.g. CAPI2 4192); real
// names do not exist. They are presented as "Parameter N" - the label Event Viewer uses for such unmapped data - at
// template-analysis time so DetailsPane, filter picklists, and column storage all read the friendly label.
public sealed class SyntheticFieldNameNormalizationTests
{
    [Fact]
    public void AllRealNames_AreLeftUnchanged()
    {
        TemplateFieldSchema schema = SchemaFor("<template><data name=\"ProcessName\"/><data name=\"UserName\"/></template>");

        Assert.Equal(["ProcessName", "UserName"], schema.AllNames);
    }

    [Fact]
    public void AllSyntheticNames_NormalizedToParameterLabels()
    {
        TemplateFieldSchema schema = SchemaFor("<template><data name=\"%1\"/><data name=\"%2\"/><data name=\"%18\"/></template>");

        Assert.Equal(["Parameter 1", "Parameter 2", "Parameter 18"], schema.AllNames);
        Assert.Equal(["Parameter 1", "Parameter 2", "Parameter 18"], schema.VisibleNames);
        Assert.True(schema.TryGetIndex(FieldNameOrdering.All, "Parameter 1", out int index));
        Assert.Equal(0, index);
        Assert.False(schema.TryGetIndex(FieldNameOrdering.All, "%1", out _));
    }

    [Fact]
    public void DuplicateSyntheticNames_AreNormalized_FirstIndexWins()
    {
        // Distinct %N are injective to distinct labels; a repeated %1 keeps first-wins lookup exactly as raw "%1" did.
        TemplateFieldSchema schema = SchemaFor("<template><data name=\"%1\"/><data name=\"%1\"/></template>");

        Assert.Equal(["Parameter 1", "Parameter 1"], schema.AllNames);
        Assert.True(schema.TryGetIndex(FieldNameOrdering.All, "Parameter 1", out int index));
        Assert.Equal(0, index);
    }

    [Fact]
    public void MixedTemplate_AnyRealName_LeavesEveryNameUntouched()
    {
        // The gate models "Windows synthesized ALL names"; one real name means it is not a classic positional template.
        TemplateFieldSchema schema = SchemaFor("<template><data name=\"%1\"/><data name=\"Real\"/></template>");

        Assert.Equal(["%1", "Real"], schema.AllNames);
    }

    [Fact]
    public void UnnamedNodePresent_FailsClosed_LeavesPlaceholdersUntouched()
    {
        // Fail closed: an unnamed/positional-only node means the template is not entirely placeholders, so nothing
        // is relabeled - the "%1" is left as-is rather than partially normalizing a non-classic template.
        TemplateFieldSchema schema = SchemaFor("<template><data name=\"%1\"/><data name=\"\"/></template>");

        Assert.Equal(["%1", ""], schema.AllNames);
    }

    [Theory]
    [InlineData("%1", "Parameter 1")]
    [InlineData("%9", "Parameter 9")]
    [InlineData("%10", "Parameter 10")]
    [InlineData("%18", "Parameter 18")]
    [InlineData("%0", "%0")]       // zero is a message terminator, never a 1-based parameter
    [InlineData("%01", "%01")]     // leading zero is non-canonical
    [InlineData("%1a", "%1a")]     // trailing non-digit
    [InlineData("% 1", "% 1")]     // embedded space
    [InlineData("%-1", "%-1")]     // sign
    [InlineData("%", "%")]         // bare percent
    [InlineData("Named", "Named")] // an ordinary field name
    public void OnlyCanonicalPositivePlaceholders_AreNormalized(string name, string expected)
    {
        TemplateFieldSchema schema = SchemaFor($"<template><data name=\"{name}\"/></template>");

        Assert.Equal([expected], schema.AllNames);
    }

    [Fact]
    public void SealedColumnStore_RoundTripsSyntheticSchemaAsParameterName()
    {
        // Full chain: TemplateAnalyzer normalizes -> the schema is interned into the SEALED store -> the reader
        // reconstructs the friendly name from the pool, and the raw "%1" key no longer resolves.
        var @event = EventDataTestFactory.CreateEventWithData(("%1", "MsSense.exe"), ("%2", "CodeSigning"));

        IEventColumnReader reader = EventColumnStore.Build(new[] { @event }, generation: 0, contentVersion: 0)
            .CreateReader(EventLogId.Create());

        Assert.True(reader.TryGetEventData(reader.LocatorAt(0), "Parameter 1", out EventFieldValue first));
        Assert.Equal("MsSense.exe", first.AsString());
        Assert.True(reader.TryGetEventData(reader.LocatorAt(0), "Parameter 2", out EventFieldValue second));
        Assert.Equal("CodeSigning", second.AsString());
        Assert.False(reader.TryGetEventData(reader.LocatorAt(0), "%1", out _));
    }

    [Fact]
    public void SyntheticLengthProviderTemplate_PreservesVisibleAllSplit()
    {
        // The length provider (%1) is excluded from Visible by its ORIGINAL name; both orderings carry the friendly label.
        TemplateFieldSchema schema = SchemaFor(
            "<template><data name=\"%1\" inType=\"win:UInt32\"/><data name=\"%2\" length=\"%1\"/></template>");

        Assert.Equal(["Parameter 1", "Parameter 2"], schema.AllNames);
        Assert.Equal(["Parameter 2"], schema.VisibleNames);
    }

    [Fact]
    public void SyntheticNames_NameOutTypeLockstepUnchanged()
    {
        var analyzer = new TemplateAnalyzer();
        const string Template = "<template><data name=\"%1\"/><data name=\"%2\"/></template>";

        TemplateFieldSchema schema = analyzer.GetTemplateInfo(Template).Schema;
        TemplateMetadata metadata = analyzer.Analyze(Template);

        Assert.Equal(metadata.AllOutTypes.Length, schema.AllNames.Length);
        Assert.Equal(metadata.VisibleOutTypes.Length, schema.VisibleNames.Length);
    }

    [Fact]
    public void UnicodeDigitPlaceholder_IsNotNormalized()
    {
        // The gate scans ASCII '0'..'9' only, so a Unicode digit neither passes the gate nor reaches the label.
        TemplateFieldSchema schema = SchemaFor("<template><data name=\"%\u0661\"/></template>"); // Arabic-Indic digit one

        Assert.Equal(["%\u0661"], schema.AllNames);
    }

    private static TemplateFieldSchema SchemaFor(string template) => new TemplateAnalyzer().GetTemplateInfo(template).Schema;
}
