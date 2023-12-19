namespace EventLogExpert.UI.UnitTests.TestUtils.Constants;

public sealed partial class Constants
{
    public const string GitHubLatestName = "EventLogExpert_23.1.1.2_x64.msix";
    public const string GitHubLatestUri =
        "https://github.com/microsoft/EventLogExpert/releases/download/v23.1.1.2/EventLogExpert_23.1.1.2_x64.msix";
    public const string GitHubLatestVersion = "v23.1.1.2";

    public const string GitHubOldVersion = "23.1.1.1";

    public const string GitHubPrereleaseName = "EventLogExpert_23.1.1.3_x64.msix";
    public const string GitHubPrereleaseUri =
        "https://github.com/microsoft/EventLogExpert/releases/download/v23.1.1.3/EventLogExpert_23.1.1.3_x64.msix";
    public const string GitHubPrereleaseVersion = "v23.1.1.3";

    public const string GitHubReleaseNotes =
        "\r\n\r\n## Changes:\r\n\r\n* f7f7aff67132dc32c92519a1bc250e1a81606e2b Fixed LF issue in App.xaml and added custom width for ultrawide monitors\r\n* 66b7d6883807a5c518ffcd59f92e07e528a5636a Updated Azure yml to .NET 8\r\n* 5b658a9c294a69cec45a14319d3851b700f0e7a2 Updated projects to .NET 8 and updated nuget packages to latest version\r\n* 3237f018153becbd80b1bc2bf7c3b30cc6dfb94f Added feature to sort events by column\r\n* d0fd869d9f9e02b6a9057052fad76d2e54c8edf1 Don't crash if filter is applied with no logs loaded\r\n* b0d34d6eb79405750f6b51cb687ccbe5dbddf40e Updated filters to run in parallel\r\n* a88d718213a62322a8e30787677c65bb050c9488 Basic filter comparison works more like advanced filter does and reduces filter creation complexity\r\n* c3b5da764c0a0e4301b2d568d373c47642159a63 Fixed issue where editing the advanced filter was not updating the event table\r\n* 3653bd8c6c213d13fcc2a399913b35dc762d19bc CombineLogs doesn't need to sort single logs\r\n* f97987ca4f69c6dfec7e56f0a586017458799036 Set TabPane to adjust max width based on the number of tabs open to prevent overflow\r\n<details><summary><b>See More</b></summary>\r\n\r\n* aa502e321a5fd5eb16c4b9e95e1da63845daef80 Reduce unnecessary sorting\r\n* c1d075cdfa6b3f3040c1ccdae5196a54ba6ad451 Updated Severity Level to support different bytes that may resolve to the same level name\r\n* 6ae91e064cec8580d03de40495a54e359943a221 Fixed sorting issue when multiple logs are first loaded with no filters applied\r\n* d549a1693896692d961f54ffcf68e2c3555e6fd9 Refactored filtering in preparation for sorting by different event fields\r\n* 3168fa7807fff0229308b01c321e05c382866a4e Removed additional columns from view menu and added context menu for enabling/disabling columns\r\n* ee5fa7a2ff56bcc147d3bd59ee0f1e1f546a5815 Changed context menu filtering options to have include/exclude as the parent option\r\n\r\nThis list of changes was [auto generated](https://dev.azure.com/CSS-Exchange-Tools/EventLogExpert/_build/results?buildId=4927&view=logs).</details>";
}
