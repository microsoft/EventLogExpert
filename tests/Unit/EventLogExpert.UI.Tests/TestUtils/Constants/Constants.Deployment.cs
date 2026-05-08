// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Tests.TestUtils.Constants;

public sealed partial class Constants
{
    public const string DownloadPath = @"C:\Downloads\update.msix";
    public const string DownloadPathUri = "file:///C:/Downloads/update.msix";
    public const string InvalidDownloadPath = "invalid://path";

    public const string ProgressString25 = "Installing: 25%";
    public const string ProgressString50 = "Installing: 50%";
    public const string ProgressString100 = "Installing: 100%";
    public const string RelaunchMessage = "Relaunch to Apply Update";

    public const string UpdateFailureTitle = "Update Failure";
    public const string UpdateFailureOk = "Ok";
}
