// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.DatabaseTools.Common.Operations;
using EventLogExpert.Localization;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.UI.DatabaseTools;
using EventLogExpert.UI.DatabaseTools.Tabs;
using EventLogExpert.UI.Tests.TestUtils;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;
using System.Reflection;

namespace EventLogExpert.UI.Tests.DatabaseTools;

public sealed class DatabaseToolsLocalizerWiringTests : BunitContext
{
    private readonly IStringLocalizer<SharedResource> _localizer = new MarkerLocalizer();

    public DatabaseToolsLocalizerWiringTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddDatabaseToolsTabDependencies();
        Services.AddMenuMocks();
    }

    public static TheoryData<Type, Type, string> EnumLocalizers => new()
    {
        { typeof(DatabaseToolsTabLocalizer), typeof(DatabaseToolsTab), nameof(DatabaseToolsTabLocalizer.Label) },
        { typeof(AutoImportModeLocalizer), typeof(AutoImportMode), nameof(AutoImportModeLocalizer.Label) },
        { typeof(DatabaseToolsOutcomeLocalizer), typeof(DatabaseToolsOutcome), nameof(DatabaseToolsOutcomeLocalizer.Label) },
        { typeof(AutoImportStateLocalizer), typeof(AutoImportState), nameof(AutoImportStateLocalizer.Label) },
        { typeof(MergeOverwriteModeLocalizer), typeof(MergeOverwriteMode), nameof(MergeOverwriteModeLocalizer.Label) },
        { typeof(DatabaseUpgradeSkipReasonLocalizer), typeof(DatabaseUpgradeSkipReason), nameof(DatabaseUpgradeSkipReasonLocalizer.Describe) },
    };

    [Fact]
    public void CreateAndShowTabs_RouteRegexPlaceholderThroughLocalizedTemplateWithInvariantRegexArgument()
    {
        var create = Render<CreateDatabaseTab>();
        var show = Render<ShowProvidersTab>();

        Assert.Equal("[[DatabaseTools_Filter_Placeholder(^Microsoft-.*)]]", create.Find("#create-filter").GetAttribute("placeholder"));
        Assert.Equal("[[DatabaseTools_Filter_Placeholder(^Microsoft-.*)]]", show.Find("#show-filter").GetAttribute("placeholder"));
    }

    [Theory]
    [MemberData(nameof(EnumLocalizers))]
    public void EnumLocalizer_MapsEveryEnumMemberToAResourceKey(Type localizerType, Type enumType, string methodName)
    {
        MethodInfo? method = localizerType.GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        foreach (object member in Enum.GetValues(enumType))
        {
            var label = Assert.IsType<string>(method!.Invoke(null, [_localizer, member]));

            Assert.StartsWith("[[", label);
            Assert.EndsWith("]]", label);
        }
    }

    [Theory]
    [InlineData(0, "[[DatabaseTools_Log_EntryCount_Many(0)]]")]
    [InlineData(1, "[[DatabaseTools_Log_EntryCount_One(1)]]")]
    public void LogView_RoutesEntryCountActionsAndOutcomeThroughLocalizer(int entryCount, string expectedCount)
    {
        ImmutableList<LogRecord> entries = Enumerable.Range(0, entryCount)
            .Select(index => new LogRecord(DateTime.UtcNow, LogLevel.Information, $"entry {index}"))
            .ToImmutableList();

        var component = Render<DatabaseToolsLogView>(parameters => parameters
            .Add(view => view.Entries, entries)
            .Add(view => view.Outcome, new DatabaseToolsResult(DatabaseToolsOutcome.Succeeded, null, TimeSpan.Zero)));

        Assert.Equal(expectedCount, component.Find(".entry-count").TextContent.Trim());
        Assert.Contains("[[DatabaseTools_Log_Copy]]", component.Markup);
        Assert.Contains("[[Modal_Export]]", component.Markup);
        Assert.Contains("[[DatabaseToolsOutcome_Succeeded]]", component.Markup);
    }

    [Theory]
    [InlineData(1, 1, "[[Db_Manage_RemoveConfirm_Accept_OneOne(1|1)]]")]
    [InlineData(1, 2, "[[Db_Manage_RemoveConfirm_Accept_OneMany(1|2)]]")]
    [InlineData(2, 1, "[[Db_Manage_RemoveConfirm_Accept_ManyOne(2|1)]]")]
    [InlineData(2, 2, "[[Db_Manage_RemoveConfirm_Accept_ManyMany(2|2)]]")]
    public void RemoveConfirmAcceptLabel_SelectsVariantForUpgradeAndDatabaseCounts(
        int upgradeCount,
        int databaseCount,
        string expectedLabel)
    {
        string label = ManageDatabasesTab.SelectRemoveConfirmAcceptLabel(_localizer, upgradeCount, databaseCount);

        Assert.Equal(expectedLabel, label);
    }
}
