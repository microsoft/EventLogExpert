// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Filtering.TestUtils.Constants;

public sealed partial class FilterTestConstants
{
    // Event property fixture values
    public const string EventComputerServer01 = "SERVER01";
    public const string EventComputerServer02 = "SERVER02";
    public const string EventDescriptionErrorOccurred = "An error occurred in the application";
    public const string EventDescriptionSuccess = "Operation completed successfully";
    public const string EventLevelError = "Error";
    public const string EventLevelInformation = "Information";
    public const string EventSourceOtherSource = "OtherSource";
    public const string EventSourceTestSource = "TestSource";
    public const string EventTaskCategorySecurity = "Security Audit";
    public const string EventTaskCategorySystem = "System";

    // Filter group names
    public const string FilterGroupDisplayName = "TestGroup";
    public const string FilterGroupName = "TestSection\\TestGroup";
    public const string FilterGroupNameNested = "TestSection\\SubSection\\TestGroup";
    public const string FilterGroupSection = "TestSection";
    public const string FilterGroupSubSection = "SubSection";

    // Numeric filter test values
    public const string FilterValue100 = "100";
    public const string FilterValue1000 = "1000";
    public const string FilterValue200 = "200";
    public const string FilterValue300 = "300";
    public const string FilterValue500 = "500";

    // Filter expression literals — comparison shapes
    public const string FilterComputerNameEqualsServer01 = "ComputerName == \"SERVER01\"";
    public const string FilterIdEquals100 = "Id == 100";
    public const string FilterIdEquals100AndLevelError = "Id == 100 && Level == \"Error\"";
    public const string FilterIdEquals100Or200 = "Id == 100 || Id == 200";
    public const string FilterIdEquals100QuotedRhs = "Id == \"100\"";
    public const string FilterIdEquals200 = "Id == 200";
    public const string FilterIdEquals999 = "Id == 999";
    public const string FilterIdGreaterThan100 = "Id > 100";
    public const string FilterIdGreaterThanOrEqual100 = "Id >= 100";
    public const string FilterIdLessThan100 = "Id < 100";
    public const string FilterIdLessThanOrEqual100 = "Id <= 100";
    public const string FilterIdMultiEquals = "(new[] {\"100\", \"200\"}).Contains(Id.ToString())";
    public const string FilterIdNotEquals100 = "Id != 100";
    public const string FilterLevelEqualsError = "Level == \"Error\"";
    public const string FilterLevelMultiEquals = "(new[] {\"Error\", \"Warning\"}).Contains(Level.ToString())";

    // Composition shapes
    public const string FilterFourConditionAnd = "Id == 100 && Source == \"TestSource\" && Level == \"Error\" && ComputerName == \"SERVER01\"";
    public const string FilterFourConditionOr = "Id == 100 || Id == 200 || Id == 300 || Id == 400";
    public const string FilterNot = "!(Source == \"TestSource\")";
    public const string FilterParenthesizedMix = "(Id == 100 || Id == 200) && Level == \"Error\"";
    public const string FilterThreeConditionAnd = "Id == 100 && Source == \"TestSource\" && Level == \"Error\"";
    public const string FilterThreeConditionOr = "Id == 100 || Id == 200 || Id == 300";
    public const string FilterTwoConditionAnd = "Id == 100 && Source == \"TestSource\"";

    // Diverged: default vs OrdinalIgnoreCase variants
    public const string FilterDescriptionContainsErrorOccurred = "Description.Contains(\"error occurred\")";
    public const string FilterDescriptionContainsErrorOccurredOic = "Description.Contains(\"error occurred\", StringComparison.OrdinalIgnoreCase)";
    public const string FilterSourceContainsTest = "Source.Contains(\"Test\")";
    public const string FilterSourceContainsTestOic = "Source.Contains(\"Test\", StringComparison.OrdinalIgnoreCase)";
    public const string FilterTaskCategoryContainsSecurity = "TaskCategory.Contains(\"Security\")";
    public const string FilterTaskCategoryContainsSecurityOic = "TaskCategory.Contains(\"Security\", StringComparison.OrdinalIgnoreCase)";
    public const string FilterXmlContainsData = "xml.Contains(\"data\")";
    public const string FilterXmlContainsDataOic = "Xml.Contains(\"data\", StringComparison.OrdinalIgnoreCase)";

    // Property-targeted shapes
    public const string FilterActivityIdContains = "ActivityId.ToString().Contains(\"abc\", StringComparison.OrdinalIgnoreCase)";
    public const string FilterActivityIdEqualsZero = "ActivityId == \"00000000-0000-0000-0000-000000000000\"";
    public const string FilterProcessIdEquals = "ProcessId == 4";
    public const string FilterRecordIdEquals = "RecordId == 1234567890123";
    public const string FilterSourceEqualsTestSource = "Source == \"TestSource\"";
    public const string FilterSourceMultiEquals = "(new[] {\"TestSource\", \"OtherSource\"}).Contains(Source)";
    public const string FilterThreadIdEquals = "ThreadId == 8";

    // Keywords shapes
    public const string FilterKeywordsAnyOfAuditOrSystem = "Keywords.Any(e => (new[] {\"Audit\", \"System\"}).Contains(e))";
    public const string FilterKeywordsContainsAudit = "Keywords.Any(e => e.Contains(\"audit\", StringComparison.OrdinalIgnoreCase))";
    public const string FilterKeywordsEqualsAudit = "Keywords.Any(e => string.Equals(e, \"Audit\", StringComparison.OrdinalIgnoreCase))";
    public const string KeywordAudit = "Audit";
    public const string KeywordSystem = "System";

    // UserId guarded shapes
    public const string FilterUserIdContainsService = "UserId != null && UserId.Value.Contains(\"S-1-5\", StringComparison.OrdinalIgnoreCase)";
    public const string FilterUserIdEqualsLocalSystem = "UserId != null && UserId.Value == \"S-1-5-18\"";
    public const string FilterUserIdNotContainsService = "UserId != null && !UserId.Value.Contains(\"S-1-5-99\", StringComparison.OrdinalIgnoreCase)";
    public const string FilterUserIdNotEqualsLocalSystem = "UserId != null && UserId.Value != \"S-1-5-18\"";
    public const string LocalSystemSddl = "S-1-5-18";

    // Escape variants
    public const string FilterDescriptionEqualsBackslash = "Description == \"a\\\\b\"";
    public const string FilterDescriptionEqualsCarriageReturn = "Description == \"a\\rb\"";
    public const string FilterDescriptionEqualsNewline = "Description == \"a\\nb\"";
    public const string FilterDescriptionEqualsQuote = "Description == \"q\\\"q\"";
    public const string FilterDescriptionEqualsTab = "Description == \"a\\tb\"";

    // Performance / real-world snapshot from PerfFilters.json
    public const string FilterPerfApplicationError = "Source == \"Application Error\"";
    public const string FilterPerfEventLog = "Id == \"6008\" && Source == \"EventLog\"";
    public const string FilterPerfKernelPower = "Source == \"Microsoft-Windows-Kernel-Power\" && TaskCategory == \"DirtyTransition\"";
    public const string FilterPerfResourceExhaustion = "Source == \"Microsoft-Windows-Resource-Exhaustion-Detector\"";
    public const string FilterPerfSystemStart = "TaskCategory == \"SystemStart\"";
    public const string FilterPerfUser32 = "Id == \"1074\" && Source == \"User32\"";
    public const string FilterPerfWerSystemErrorReporting = "Id == \"1001\" && Source == \"Microsoft-Windows-WER-SystemErrorReporting\"";

    // Invalid expressions
    public const string FilterInvalidProperty = "InvalidProperty == 100";
    public const string FilterInvalidValue = "Id == invalid";
}