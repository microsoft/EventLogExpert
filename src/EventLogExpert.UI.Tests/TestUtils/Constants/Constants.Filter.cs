// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Tests.TestUtils.Constants;

public sealed partial class Constants
{
    // Filter expression strings
    public const string FilterIdEquals100 = "Id == 100";
    public const string FilterIdEquals200 = "Id == 200";
    public const string FilterIdNotEquals100 = "Id != 100";
    public const string FilterIdGreaterThan100 = "Id > 100";
    public const string FilterIdEquals100Or200 = "Id == 100 || Id == 200";
    public const string FilterIdEquals100AndLevelError = "Id == 100 && Level == \"Error\"";
    public const string FilterLevelEqualsError = "Level == \"Error\"";
    public const string FilterSourceContainsTest = "Source.Contains(\"Test\")";
    public const string FilterDescriptionContainsErrorOccurred = "Description.Contains(\"error occurred\")";
    public const string FilterComputerNameEqualsServer01 = "ComputerName == \"SERVER01\"";
    public const string FilterTaskCategoryContainsSecurity = "TaskCategory.Contains(\"Security\")";
    public const string FilterInvalidProperty = "InvalidProperty == 100";
    public const string FilterInvalidValue = "Id == invalid";

    // Event property values for testing
    public const string EventLevelError = "Error";
    public const string EventLevelInformation = "Information";
    public const string EventSourceTestSource = "TestSource";
    public const string EventSourceOtherSource = "OtherSource";
    public const string EventDescriptionErrorOccurred = "An error occurred in the application";
    public const string EventDescriptionSuccess = "Operation completed successfully";
    public const string EventComputerServer01 = "SERVER01";
    public const string EventComputerServer02 = "SERVER02";
    public const string EventTaskCategorySecurity = "Security Audit";
    public const string EventTaskCategorySystem = "System";

    // Filter data test values
    public const string FilterValue100 = "100";
    public const string FilterValue200 = "200";
    public const string FilterValue300 = "300";
    public const string FilterValue500 = "500";
    public const string FilterValue1000 = "1000";
}
