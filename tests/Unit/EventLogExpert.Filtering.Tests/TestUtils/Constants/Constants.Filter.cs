// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Filtering.Tests.TestUtils.Constants;

/// <summary>
///     Filter expression literals shared across multiple test classes. Mirrors EventLogExpert.UI.Tests'
///     <c>Constants.Filter</c> shape so the parity suite covers the same expressions exercised by upstream UI tests plus
///     the formatter shapes and the Advanced free-text variants.
/// </summary>
public sealed partial class Constants
{
    public const string EventComputerServer01 = "SERVER01";
    public const string EventLevelError = "Error";
    public const string EventLevelInformation = "Information";
    public const string EventSourceTestSource = "TestSource";
    public const string EventTaskCategorySystem = "System";
    public const string FilterActivityIdContains =
        "ActivityId.ToString().Contains(\"abc\", StringComparison.OrdinalIgnoreCase)";

    // ActivityId / RecordId / ProcessId / ThreadId shapes.
    public const string FilterActivityIdEqualsZero =
        "ActivityId == \"00000000-0000-0000-0000-000000000000\"";
    public const string FilterComputerNameEqualsServer01 = "ComputerName == \"SERVER01\"";
    public const string FilterDescriptionContainsErrorOccurred =
        "Description.Contains(\"error occurred\", StringComparison.OrdinalIgnoreCase)";

    // Escape variants.
    public const string FilterDescriptionEqualsBackslash = "Description == \"a\\\\b\"";
    public const string FilterDescriptionEqualsCarriageReturn = "Description == \"a\\rb\"";
    public const string FilterDescriptionEqualsNewline = "Description == \"a\\nb\"";
    public const string FilterDescriptionEqualsQuote = "Description == \"q\\\"q\"";
    public const string FilterDescriptionEqualsTab = "Description == \"a\\tb\"";
    public const string FilterFourConditionAnd =
        "Id == 100 && Source == \"TestSource\" && Level == \"Error\" && ComputerName == \"SERVER01\"";
    public const string FilterFourConditionOr = "Id == 100 || Id == 200 || Id == 300 || Id == 400";

    // Numeric formatter shapes.
    public const string FilterIdEquals100 = "Id == 100";
    public const string FilterIdEquals100AndLevelError = "Id == 100 && Level == \"Error\"";
    public const string FilterIdEquals100Or200 = "Id == 100 || Id == 200";
    public const string FilterIdEquals100QuotedRhs = "Id == \"100\"";
    public const string FilterIdEquals200 = "Id == 200";
    public const string FilterIdGreaterThan100 = "Id > 100";
    public const string FilterIdGreaterThanOrEqual100 = "Id >= 100";
    public const string FilterIdLessThan100 = "Id < 100";
    public const string FilterIdLessThanOrEqual100 = "Id <= 100";

    // Multi-equals shapes.
    public const string FilterIdMultiEquals = "(new[] {\"100\", \"200\"}).Contains(Id.ToString())";
    public const string FilterIdNotEquals100 = "Id != 100";
    public const string FilterKeywordsAnyOfAuditOrSystem =
        "Keywords.Any(e => (new[] {\"Audit\", \"System\"}).Contains(e))";
    public const string FilterKeywordsContainsAudit =
        "Keywords.Any(e => e.Contains(\"audit\", StringComparison.OrdinalIgnoreCase))";

    // Keywords shapes.
    public const string FilterKeywordsEqualsAudit =
        "Keywords.Any(e => string.Equals(e, \"Audit\", StringComparison.OrdinalIgnoreCase))";

    // String formatter shapes.
    public const string FilterLevelEqualsError = "Level == \"Error\"";
    public const string FilterLevelMultiEquals = "(new[] {\"Error\", \"Warning\"}).Contains(Level.ToString())";
    public const string FilterNot = "!(Source == \"TestSource\")";
    public const string FilterParenthesizedMix = "(Id == 100 || Id == 200) && Level == \"Error\"";
    public const string FilterPerfApplicationError = "Source == \"Application Error\"";
    public const string FilterPerfEventLog = "Id == \"6008\" && Source == \"EventLog\"";
    public const string FilterPerfKernelPower =
        "Source == \"Microsoft-Windows-Kernel-Power\" && TaskCategory == \"DirtyTransition\"";
    public const string FilterPerfResourceExhaustion = "Source == \"Microsoft-Windows-Resource-Exhaustion-Detector\"";
    public const string FilterPerfSystemStart = "TaskCategory == \"SystemStart\"";
    public const string FilterPerfUser32 = "Id == \"1074\" && Source == \"User32\"";

    // Real-world snapshot from PerfFilters.json (verbatim user output).
    public const string FilterPerfWerSystemErrorReporting =
        "Id == \"1001\" && Source == \"Microsoft-Windows-WER-SystemErrorReporting\"";
    public const string FilterProcessIdEquals = "ProcessId == 4";
    public const string FilterRecordIdEquals = "RecordId == 1234567890123";
    public const string FilterSourceContainsTest = "Source.Contains(\"Test\")";
    public const string FilterSourceContainsTestOic = "Source.Contains(\"Test\", StringComparison.OrdinalIgnoreCase)";
    public const string FilterSourceEqualsTestSource = "Source == \"TestSource\"";
    public const string FilterSourceMultiEquals = "(new[] {\"TestSource\", \"OtherSource\"}).Contains(Source)";
    public const string FilterTaskCategoryContainsSecurity =
        "TaskCategory.Contains(\"Security\", StringComparison.OrdinalIgnoreCase)";
    public const string FilterThreadIdEquals = "ThreadId == 8";
    public const string FilterThreeConditionAnd = "Id == 100 && Source == \"TestSource\" && Level == \"Error\"";
    public const string FilterThreeConditionOr = "Id == 100 || Id == 200 || Id == 300";

    // Composition shapes.
    public const string FilterTwoConditionAnd = "Id == 100 && Source == \"TestSource\"";
    public const string FilterUserIdContainsService =
        "UserId != null && UserId.Value.Contains(\"S-1-5\", StringComparison.OrdinalIgnoreCase)";

    // UserId guarded shapes.
    public const string FilterUserIdEqualsLocalSystem = "UserId != null && UserId.Value == \"S-1-5-18\"";
    public const string FilterUserIdNotEqualsLocalSystem = "UserId != null && UserId.Value != \"S-1-5-18\"";
    public const string FilterXmlContainsData = "Xml.Contains(\"data\", StringComparison.OrdinalIgnoreCase)";
    public const string KeywordAudit = "Audit";
    public const string KeywordSystem = "System";

    public const string LocalSystemSddl = "S-1-5-18";
}
