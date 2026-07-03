// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.DatabaseTools.Common.Operations;
using EventLogExpert.DatabaseTools.CreateDatabase;
using EventLogExpert.Eventing.PublisherMetadata.Offline;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Common.Versioning;
using EventLogExpert.Runtime.DatabaseTools;
using EventLogExpert.Runtime.DatabaseTools.Elevation;
using EventLogExpert.Runtime.DebugLog;
using EventLogExpert.UI.DatabaseTools.Tabs;
using EventLogExpert.UI.Tests.TestUtils;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace EventLogExpert.UI.Tests.DatabaseTools.Tabs;

public sealed class CreateDatabaseTabTests : BunitContext
{
    public CreateDatabaseTabTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddDatabaseToolsTabDependencies();
        Services.AddMenuMocks();
    }

    [Fact]
    public void AdminProcess_HidesProtectedProvidersChoice_RunNotShielded()
    {
        // Elevated processes already read protected providers in-process, so no UAC affordance is shown.
        Services.GetRequiredService<ICurrentVersionProvider>().IsAdmin.Returns(true);

        var component = Render<CreateDatabaseTab>();

        Assert.Empty(component.FindAll("#create-include-protected"));
        Assert.Empty(component.FindAll(".bi-shield-lock"));
    }

    [Fact]
    public void AfterFailedRun_DoesNotShowImportDatabaseButton()
    {
        ConfigureCreateOutcome(DatabaseToolsOutcome.Failed);
        var component = Render<CreateDatabaseTab>();
        component.Find("#create-target-path").Input(@"C:\out.db");

        component.Find(".button-green").Click();

        component.WaitForAssertion(() =>
        {
            Assert.NotEmpty(component.FindAll(".outcome-failed"));
            Assert.Empty(component.FindAll(".bi-database-add"));
        });
    }

    [Fact]
    public void AfterSuccessfulRun_ShowsImportDatabaseButton()
    {
        ConfigureCreateOutcome(DatabaseToolsOutcome.Succeeded);
        var component = Render<CreateDatabaseTab>();
        component.Find("#create-target-path").Input(@"C:\out.db");

        component.Find(".button-green").Click();

        component.WaitForAssertion(() =>
        {
            Assert.NotEmpty(component.FindAll(".outcome-succeeded"));
            Assert.NotEmpty(component.FindAll(".bi-database-add"));
        });
    }

    [Fact]
    public async Task AutoImportImportAndEnable_RequestsEnabledImport()
    {
        // The create mock writes during dispatch so auto-import sees the file without tripping overwrite confirmation.
        var writtenDatabasePaths = ConfigureCreateSucceededWritingDatabaseFile();

        var targetPath = BuildTempDatabaseTargetPath();
        try
        {
            bool? requestedEnable = null;
            var component = Render<CreateDatabaseTab>(parameters => parameters
                .AddCascadingValue("RequestAutoImport", (Func<string, bool, Task>)((_, enable) =>
                {
                    requestedEnable = enable;
                    return Task.CompletedTask;
                })));

            component.Find("#create-target-path").Input(targetPath);
            await component.FindAll("[role='option']").Single(option => option.TextContent.Trim() == "Import and enable").MouseDownAsync(new MouseEventArgs());

            component.Find(".button-green").Click();

            component.WaitForAssertion(() => Assert.True(requestedEnable));
        }
        finally
        {
            DeleteFilesIfPresent(writtenDatabasePaths);
        }
    }

    [Fact]
    public async Task AutoImportSucceededButProducedFileMissing_DoesNotRequestImport()
    {
        ConfigureCreateOutcome(DatabaseToolsOutcome.Succeeded);

        var importRequested = false;
        var component = Render<CreateDatabaseTab>(parameters => parameters
            .AddCascadingValue("RequestAutoImport", (Func<string, bool, Task>)((_, _) =>
            {
                importRequested = true;
                return Task.CompletedTask;
            })));

        // Random subdirectory keeps the mocked success artifact absent.
        var missingDatabasePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.db");
        component.Find("#create-target-path").Input(missingDatabasePath);
        await component.FindAll("[role='option']").Single(option => option.TextContent.Trim() == "Import database").MouseDownAsync(new MouseEventArgs());

        component.Find(".button-green").Click();

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll(".outcome-succeeded")));
        Assert.False(importRequested);
    }

    [Fact]
    public async Task DuringManualImport_DisablesRunButton()
    {
        // Manual import sets _autoImportState=Importing while IsRunning=false, so Run must still be disabled.
        ConfigureCreateOutcome(DatabaseToolsOutcome.Succeeded);

        var importGate = new TaskCompletionSource();
        var component = Render<CreateDatabaseTab>(parameters => parameters
            .AddCascadingValue("RequestAutoImport", (Func<string, bool, Task>)((_, _) => importGate.Task)));

        component.Find("#create-target-path").Input(@"C:\out.db");

        component.Find(".button-green").Click();
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll(".bi-database-add")));

        component.Find(".bi-database-add").ParentElement!.Click();

        component.WaitForAssertion(() =>
        {
            Assert.Contains("Importing database", component.Markup);
            Assert.True(component.Find(".button-green").HasAttribute("disabled"));
        });

        await component.InvokeAsync(importGate.SetResult);

        component.WaitForAssertion(() => Assert.Contains("Database imported", component.Markup));
    }

    [Fact]
    public async Task DuringPostSuccessAutoImport_OffersDisabledRunNotCancel()
    {
        // Auto-import runs after create succeeds but before IsRunning clears, so Cancel could only target a finished run.
        var writtenDatabasePaths = ConfigureCreateSucceededWritingDatabaseFile();

        var targetPath = BuildTempDatabaseTargetPath();
        try
        {
            var importGate = new TaskCompletionSource();
            var component = Render<CreateDatabaseTab>(parameters => parameters
                .AddCascadingValue("RequestAutoImport", (Func<string, bool, Task>)((_, _) => importGate.Task)));

            component.Find("#create-target-path").Input(targetPath);
            await component.FindAll("[role='option']").Single(option => option.TextContent.Trim() == "Import database").MouseDownAsync(new MouseEventArgs());

            component.Find(".button-green").Click();

            component.WaitForAssertion(() =>
            {
                Assert.Contains("Importing database", component.Markup);
                Assert.Empty(component.FindAll(".button-red"));
                Assert.True(component.Find(".button-green").HasAttribute("disabled"));
            });

            await component.InvokeAsync(importGate.SetResult);

            component.WaitForAssertion(() =>
            {
                Assert.Contains("Database imported", component.Markup);
                Assert.False(component.Find(".button-green").HasAttribute("disabled"));
            });
        }
        finally
        {
            DeleteFilesIfPresent(writtenDatabasePaths);
        }
    }

    [Fact]
    public void HidesImageIndex_WhenSourceIsProviderFile()
    {
        var component = Render<CreateDatabaseTab>();

        component.Find("#create-source-path").Input(@"C:\providers.db");

        Assert.Empty(component.FindAll("#create-wim-index"));
    }

    [Fact]
    public void HidesImageIndex_WhenSourceIsVhdxImage()
    {
        var component = Render<CreateDatabaseTab>();

        // VHDX images are read directly from their Windows partition, so no image index applies.
        component.Find("#create-source-path").Input(@"C:\images\disk.vhdx");

        Assert.Empty(component.FindAll("#create-wim-index"));
    }

    [Fact]
    public void IncludeProtectedProviders_ShieldsRunAndRoutesThroughElevatedHelper()
    {
        var elevatedRunner = ConfigureElevatedCreateSucceeded();

        var component = Render<CreateDatabaseTab>();
        component.Find("#create-target-path").Input(@"C:\out.db");
        component.Find("#create-include-protected").Change(true);

        Assert.NotEmpty(component.FindAll(".bi-shield-lock"));

        component.Find(".button-green").Click();

        component.WaitForAssertion(() => AssertCreateRoutedThroughElevatedHelper(elevatedRunner));
    }

    [Fact]
    public void LoadEditions_FillsTheEmptyIndexBoxWithTheFirstEdition()
    {
        ConfigureEditionsListed(
            new WimImageEntry(2, "Windows Server 2025 Standard", "ServerStandard", null),
            new WimImageEntry(4, "Windows Server 2025 Datacenter", "ServerDatacenter", null));

        var component = Render<CreateDatabaseTab>();
        component.Find("#create-source-path").Input(@"C:\images\install.wim");

        Assert.True(string.IsNullOrEmpty(component.Find("#create-wim-index").GetAttribute("value")));

        component.FindAll("button").Single(button => button.TextContent.Contains("Load editions")).Click();

        component.WaitForAssertion(() => Assert.Equal(
            "2: ServerStandard (Windows Server 2025 Standard)",
            component.Find("#create-wim-index").GetAttribute("value")));
    }

    [Fact]
    public void LoadEditions_RoutesLogsThroughTheOperationLogProgressFactory()
    {
        // F1: the edition probe crosses IPC to the elevated helper, so it must build its sink via the
        // OperationLogProgressFactory (which tees to the shared file sink under this tab's category), not a bare
        // UI-only Progress.
        ConfigureEditionsListed(new WimImageEntry(2, "Windows Server 2025 Standard", "ServerStandard", null));

        var component = Render<CreateDatabaseTab>();
        component.Find("#create-source-path").Input(@"C:\images\install.wim");

        component.FindAll("button").Single(button => button.TextContent.Contains("Load editions")).Click();

        component.WaitForAssertion(() => Services.GetRequiredService<IOperationLogProgressFactory>()
            .Received()
            .Create(Arg.Any<IProgress<LogRecord>>(), LogCategories.DatabaseToolsCreate, Arg.Any<bool>()));
    }

    [Fact]
    public void LocalScanNonAdmin_OffersProtectedProvidersChoice_RunNotShielded()
    {
        var component = Render<CreateDatabaseTab>();

        Assert.NotNull(component.Find("#create-include-protected"));
        Assert.Empty(component.FindAll(".bi-shield-lock"));
        Assert.NotEmpty(component.FindAll(".bi-play-fill"));
    }

    [Fact]
    public void OfflineImageNonAdmin_ShieldsRun_HidesProtectedProvidersChoice()
    {
        // Offline image extraction always requires elevation, so the opt-in checkbox would be meaningless.
        var component = Render<CreateDatabaseTab>();

        component.Find("#create-source-path").Input(@"C:\images\install.wim");

        Assert.NotEmpty(component.FindAll(".bi-shield-lock"));
        Assert.Empty(component.FindAll("#create-include-protected"));
    }

    [Fact]
    public void Renders_FilterErrorState_WhenInvalidRegexEntered()
    {
        var component = Render<CreateDatabaseTab>();

        var filter = component.Find("#create-filter");
        filter.Input("[unterminated");

        Assert.NotEmpty(component.FindAll(".filter-error"));
    }

    [Fact]
    public void Renders_HappyPath_WithExpectedFormFields()
    {
        var component = Render<CreateDatabaseTab>();

        Assert.NotNull(component.Find("#create-target-path"));
        Assert.NotNull(component.Find("#create-source-path"));
        Assert.NotNull(component.Find("#create-filter"));
        Assert.NotNull(component.Find("#create-skip-path"));
    }

    [Fact]
    public void RunButton_DisabledInitially_BecauseTargetPathIsEmpty()
    {
        var component = Render<CreateDatabaseTab>();

        var runButton = component.Find(".button-green");
        Assert.True(runButton.HasAttribute("disabled"));
    }

    [Fact]
    public void RunButton_ExposesElevationToScreenReaders_OnlyWhenElevating()
    {
        // The aria-hidden shield needs aria-describedby as the screen-reader elevation cue.
        var component = Render<CreateDatabaseTab>();
        component.Find("#create-target-path").Input(@"C:\out.db");

        Assert.Null(component.Find(".button-green").GetAttribute("aria-describedby"));
        Assert.Empty(component.FindAll("#create-run-elevation-help"));

        component.Find("#create-include-protected").Change(true);

        Assert.Equal("create-run-elevation-help", component.Find(".button-green").GetAttribute("aria-describedby"));
        Assert.Contains("administrator access", component.Find("#create-run-elevation-help").TextContent);
    }

    [Fact]
    public void ShowsImageIndex_WhenSourceIsWimImage()
    {
        var component = Render<CreateDatabaseTab>();

        component.Find("#create-source-path").Input(@"C:\images\install.wim");

        Assert.NotNull(component.Find("#create-wim-index"));
    }

    private static string BuildTempDatabaseTargetPath() =>
        Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");

    private static void DeleteFilesIfPresent(IEnumerable<string> paths)
    {
        foreach (var path in paths) { File.Delete(path); }
    }

    private void AssertCreateRoutedThroughElevatedHelper(IElevatedDatabaseToolsRunner elevatedRunner)
    {
        elevatedRunner.ReceivedWithAnyArgs(1).CreateAsync(default!, default!, default, default);
        Services.GetRequiredService<IDatabaseToolsService>()
            .DidNotReceiveWithAnyArgs().CreateAsync(default!, default!, default, default);
    }

    private void ConfigureCreateOutcome(DatabaseToolsOutcome outcome) =>
        Services.GetRequiredService<IDatabaseToolsService>()
            .CreateAsync(default!, default!, default, default)
            .ReturnsForAnyArgs(Task.FromResult(
                new DatabaseToolsResult(outcome, outcome == DatabaseToolsOutcome.Failed ? "probe blocked" : null, TimeSpan.Zero)));

    // Writes during dispatch so File.Exists passes auto-import without pre-triggering overwrite confirmation.
    private List<string> ConfigureCreateSucceededWritingDatabaseFile()
    {
        var writtenDatabasePaths = new List<string>();
        Services.GetRequiredService<IDatabaseToolsService>()
            .CreateAsync(default!, default!, default, default)
            .ReturnsForAnyArgs(callInfo =>
            {
                var targetPath = callInfo.Arg<CreateDatabaseRequest>().TargetPath;
                File.WriteAllText(targetPath, string.Empty);
                writtenDatabasePaths.Add(targetPath);
                return Task.FromResult(new DatabaseToolsResult(DatabaseToolsOutcome.Succeeded, null, TimeSpan.Zero));
            });
        return writtenDatabasePaths;
    }

    private void ConfigureEditionsListed(params WimImageEntry[] editions) =>
        Services.GetRequiredService<IElevatedDatabaseToolsRunner>()
            .ListImageEditionsAsync(default!, default!, default)
            .ReturnsForAnyArgs(Task.FromResult(new OfflineImageEditionsResult(
                DatabaseToolsOutcome.Succeeded,
                new WimImageList(WimImageListStatus.Ok, editions),
                FailureSummary: null)));

    private IElevatedDatabaseToolsRunner ConfigureElevatedCreateSucceeded()
    {
        var elevatedRunner = Services.GetRequiredService<IElevatedDatabaseToolsRunner>();
        elevatedRunner.CreateAsync(default!, default!, default, default)
            .ReturnsForAnyArgs(Task.FromResult(new DatabaseToolsResult(DatabaseToolsOutcome.Succeeded, null, TimeSpan.Zero)));
        return elevatedRunner;
    }
}
