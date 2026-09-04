// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Resolvers;
using EventLogExpert.Runtime.Common.Clipboard;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.Settings;
using EventLogExpert.UI.LogTable;
using EventLogExpert.UI.Tests.TestUtils;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using System.Reflection;
using System.Xml.Linq;

namespace EventLogExpert.UI.Tests.LogTable;

public sealed class LogTableLocalizerWiringTests
{
    private readonly MarkerLocalizer _localizer = new();

    public static TheoryData<bool, bool, bool, string, string, string> CellFilterCases => new()
    {
        { false, false, true, "Event ID", "1000", "[[CellFilter_IncludeWhereEquals(Event ID|1000)]]" },
        { false, true, true, "Keywords", "Audit", "[[CellFilter_IncludeWhereHas(Keywords|Audit)]]" },
        { true, false, true, "Source", "Provider", "[[CellFilter_ExcludeWhereEquals(Source|Provider)]]" },
        { true, true, true, "Keywords", "Audit", "[[CellFilter_ExcludeWhereHas(Keywords|Audit)]]" },
        { false, false, false, "Source", string.Empty, "[[CellFilter_IncludeWhereNoValue(Source)]]" },
        { true, true, false, "Keywords", string.Empty, "[[CellFilter_ExcludeWhereNoValue(Keywords)]]" },
    };

    public static TheoryData<EventCopyFullField, string> EventCopyFieldCases => new()
    {
        { EventCopyFullField.LogName, "[[Copy_Full_LogName(value)]]" },
        { EventCopyFullField.Source, "[[Copy_Full_Source(value)]]" },
        { EventCopyFullField.Date, "[[Copy_Full_Date(value)]]" },
        { EventCopyFullField.EventId, "[[Copy_Full_EventId(value)]]" },
        { EventCopyFullField.TaskCategory, "[[Copy_Full_TaskCategory(value)]]" },
        { EventCopyFullField.Level, "[[Copy_Full_Level(value)]]" },
        { EventCopyFullField.Keywords, "[[Copy_Full_Keywords(value)]]" },
        { EventCopyFullField.User, "[[Copy_Full_User(value)]]" },
        { EventCopyFullField.UserSid, "[[Copy_Full_UserSid(value)]]" },
        { EventCopyFullField.Computer, "[[Copy_Full_Computer(value)]]" },
        { EventCopyFullField.DescriptionHeader, "[[Copy_Full_DescriptionHeader]]" },
        { EventCopyFullField.EventXmlHeader, "[[Copy_Full_EventXmlHeader]]" },
    };

    [Fact]
    public void DateColumnHeader_RoutesBothTimeZoneBranchesThroughLocalizer()
    {
        var pane = new LogTablePane();
        SetInjected(pane, "Localizer", _localizer);

        var settings = Substitute.For<ISettingsService>();
        settings.TimeZoneInfo.Returns(TimeZoneInfo.Local);
        SetInjected(pane, "Settings", settings);
        Assert.Equal("[[Table_ColumnHeader_DateAndTime]]", InvokeDateHeader(pane));

        TimeZoneInfo alternateZone = TimeZoneInfo.CreateCustomTimeZone(
            "l4-marker-zone",
            TimeSpan.FromHours(3),
            "Marker Zone Standard Time",
            "Marker Zone Standard Time");
        Assert.NotEqual(TimeZoneInfo.Local, alternateZone);
        settings.TimeZoneInfo.Returns(alternateZone);
        Assert.Equal($"[[Table_ColumnHeader_DateAndTimeWithZone({alternateZone.DisplayName.Split(' ')[0]})]]", InvokeDateHeader(pane));
    }

    [Fact]
    public void EventCopyFormatterComposition_UsesUiEventCopyTextRegistration()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddEventLogRuntime();
        services.AddEventLogLocalization();
        services.AddEventLogUIServices();
        services.AddSingleton(Substitute.For<IEventDetailResolver>());
        services.AddSingleton(Substitute.For<IEventXmlResolver>());

        using var provider = services.BuildServiceProvider();

        Assert.IsType<EventCopyText>(provider.GetRequiredService<IEventCopyText>());
        Assert.NotNull(provider.GetRequiredService<IEventCopyFormatter>());
    }

    [Fact]
    public void EventCopyFullField_CasesCoverEveryEnumMember()
    {
        EventCopyFullField[] tested = [EventCopyFullField.LogName, EventCopyFullField.Source, EventCopyFullField.Date, EventCopyFullField.EventId, EventCopyFullField.TaskCategory, EventCopyFullField.Level, EventCopyFullField.Keywords, EventCopyFullField.User, EventCopyFullField.UserSid, EventCopyFullField.Computer, EventCopyFullField.DescriptionHeader, EventCopyFullField.EventXmlHeader];
        tested = [.. tested.Order()];
        var defined = Enum.GetValues<EventCopyFullField>().Order().ToArray();

        Assert.Equal(defined, tested);
    }

    [Fact]
    public void EventCopyText_FieldLine_IgnoresHeadingValues()
    {
        EventCopyText text = new(_localizer);

        Assert.Equal("[[Copy_Full_DescriptionHeader]]", text.FieldLine(EventCopyFullField.DescriptionHeader, "ignored"));
        Assert.Equal("[[Copy_Full_EventXmlHeader]]", text.FieldLine(EventCopyFullField.EventXmlHeader, "ignored"));
    }

    [Theory]
    [MemberData(nameof(EventCopyFieldCases))]
    public void EventCopyText_FieldLine_MapsEveryFieldToLiteralKey(EventCopyFullField field, string expected)
    {
        EventCopyText text = new(_localizer);

        Assert.Equal(expected, text.FieldLine(field, "value"));
    }

    [Fact]
    public void EventCopyText_HeadingResources_HaveNoPlaceholderOrTrailingWhitespace()
    {
        var values = LoadSharedResourceData()
            .Where(data => data.Attribute("name")?.Value is "Copy_Full_DescriptionHeader" or "Copy_Full_EventXmlHeader")
            .ToDictionary(data => data.Attribute("name")!.Value, data => data.Element("value")!.Value);

        Assert.Equal(2, values.Count);
        foreach (string value in values.Values)
        {
            Assert.DoesNotContain("{0}", value, StringComparison.Ordinal);
            Assert.Equal(value.TrimEnd(), value);
        }
    }

    [Theory]
    [MemberData(nameof(CellFilterCases))]
    public void LogTableCellFilterLocalizer_ChoosesWholeStringTemplate(bool exclude, bool isKeywords, bool hasValue, string column, string value, string expected)
    {
        string actual = LogTableCellFilterLocalizer.Describe(_localizer, exclude, isKeywords, hasValue, column, value);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void TabBarResources_UseRealLineFeedsAndEllipsisCharacters()
    {
        var values = LoadSharedResourceData()
            .Where(data => data.Attribute("name")?.Value is
                "TabBar_Tooltip_File" or
                "TabBar_Tooltip_Live" or
                "TabBar_Menu_Rename" or
                "TabBar_Menu_NewGroup" or
                "TabBar_Menu_NewGroupFromTab" or
                "Table_LoadingEvents" or
                "Table_ReorderingEvents")
            .ToDictionary(data => data.Attribute("name")!.Value, data => data.Element("value")!.Value);

        Assert.Equal(2, values["TabBar_Tooltip_File"].Count(character => character == '\n'));
        Assert.Equal(1, values["TabBar_Tooltip_Live"].Count(character => character == '\n'));
        Assert.DoesNotContain(@"\n", values["TabBar_Tooltip_File"], StringComparison.Ordinal);
        Assert.DoesNotContain(@"\n", values["TabBar_Tooltip_Live"], StringComparison.Ordinal);

        foreach (var key in new[] { "TabBar_Menu_Rename", "TabBar_Menu_NewGroup", "TabBar_Menu_NewGroupFromTab", "Table_LoadingEvents", "Table_ReorderingEvents" })
        {
            Assert.Contains('…', values[key]);
            Assert.DoesNotContain("...", values[key], StringComparison.Ordinal);
        }
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "EventLogExpert.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory.FullName;
    }

    private static string InvokeDateHeader(LogTablePane pane)
    {
        var method = typeof(LogTablePane).GetMethod("GetDateColumnHeader", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return Assert.IsType<string>(method.Invoke(pane, []));
    }

    private static IEnumerable<XElement> LoadSharedResourceData()
    {
        string root = FindRepositoryRoot();
        string path = Path.Combine(root, "src", "EventLogExpert.Localization", "Resources", "SharedResource.resx");
        return XDocument.Load(path).Root!.Elements("data");
    }

    private static void SetInjected<TValue>(LogTablePane pane, string name, TValue value)
    {
        var property = typeof(LogTablePane).GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(property);
        property.SetValue(pane, value);
    }
}
