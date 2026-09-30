// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.Tests.TestUtils;
using System.Globalization;

namespace EventLogExpert.Runtime.Tests.LogTable;

[Collection(CultureSensitiveCollection.Name)]
public sealed class ColumnDescriptorsCultureTests
{
    private static readonly ColumnFormatContext s_context = new(TimeZoneInfo.Utc);

    [Theory]
    [InlineData(ColumnName.RecordId)]
    [InlineData(ColumnName.EventId)]
    [InlineData(ColumnName.ProcessId)]
    [InlineData(ColumnName.ThreadId)]
    public void IdentifierColumns_DoNotGroupLargeValuesUnderCurrentCulture(ColumnName column)
    {
        CultureInfo culture = CultureInfo.GetCultureInfo("fi-FI");

        string text = RunUnderCulture(culture, () => ColumnDescriptors.GetCellText(LargeIdentifierEvent(), column, s_context));

        Assert.Equal("1234567", text);
        Assert.DoesNotContain(culture.NumberFormat.NumberGroupSeparator, text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ColumnName.RecordId, "-1234567")]
    [InlineData(ColumnName.EventId, "-1234567")]
    [InlineData(ColumnName.ProcessId, "-1234567")]
    [InlineData(ColumnName.ThreadId, "-1234567")]
    public void IdentifierColumns_UseInvariantNegativeSignUnderCustomCulture(ColumnName column, string expected)
    {
        CultureInfo culture = CultureInfo.GetCultureInfo("fi-FI").Clone() as CultureInfo ?? throw new InvalidOperationException("Expected cloneable culture.");
        culture.NumberFormat.NegativeSign = "[MINUS]";

        string text = RunUnderCulture(culture, () => ColumnDescriptors.GetCellText(NegativeIdentifierEvent(), column, s_context));

        Assert.Equal(expected, text);
        Assert.DoesNotContain(culture.NumberFormat.NegativeSign, text, StringComparison.Ordinal);
    }

    private static ResolvedEvent LargeIdentifierEvent() =>
        new("Application", LogPathType.Channel)
        {
            RecordId = 1234567,
            Id = 1234567,
            ProcessId = 1234567,
            ThreadId = 1234567
        };

    private static ResolvedEvent NegativeIdentifierEvent() =>
        new("Application", LogPathType.Channel)
        {
            RecordId = -1234567,
            Id = -1234567,
            ProcessId = -1234567,
            ThreadId = -1234567
        };

    private static string RunUnderCulture(CultureInfo culture, Func<string> build)
    {
        CultureInfo priorCulture = CultureInfo.CurrentCulture;
        CultureInfo priorUiCulture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
            return build();
        }
        finally
        {
            CultureInfo.CurrentCulture = priorCulture;
            CultureInfo.CurrentUICulture = priorUiCulture;
        }
    }
}
