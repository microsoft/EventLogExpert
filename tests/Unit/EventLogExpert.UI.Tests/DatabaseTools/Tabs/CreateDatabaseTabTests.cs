// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.DatabaseTools.Common.Operations;
using EventLogExpert.DatabaseTools.CreateDatabase;
using EventLogExpert.Runtime.Common.Versioning;
using EventLogExpert.Runtime.DatabaseTools;
using EventLogExpert.Runtime.DatabaseTools.Elevation;
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
        // An elevated process already reads protected providers in-process and never prompts for UAC, so neither the
        // opt-in checkbox nor the Run shield appears.
        Services.GetRequiredService<ICurrentVersionProvider>().IsAdmin.Returns(true);

        var component = Render<CreateDatabaseTab>();

        Assert.Empty(component.FindAll("#create-include-protected"));
        Assert.Empty(component.FindAll(".bi-shield-lock"));
    }

    [Fact]
    public void AfterFailedRun_DoesNotShowImportDatabaseButton()
    {
        // A failed create must not offer "Import database": ProducedDatabasePath is gated on a Succeeded outcome, so the
        // import affordance never targets a database the run did not actually produce.
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

        // The post-run "Import database" affordance (its bi-database-add icon) appears once the run reports success.
        component.WaitForAssertion(() =>
        {
            Assert.NotEmpty(component.FindAll(".outcome-succeeded"));
            Assert.NotEmpty(component.FindAll(".bi-database-add"));
        });
    }

    [Fact]
    public async Task AutoImportImportAndEnable_RequestsEnabledImport()
    {
        // Selecting "Import and enable" must drive the post-run auto-import with enable=true. Exercises the new
        // ValueSelect end-to-end: @bind-Value updates AutoImportMode and the successful run forwards the enable flag.
        // OnRunSucceededAsync skips auto-import for a Succeeded run whose artifact is absent, so the create mock writes
        // the target as a real run would; the file stays absent until dispatch, keeping the overwrite-confirm gate quiet.
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
        // A Succeeded create normally writes the target, but if that artifact is missing at completion the tab must NOT
        // auto-import a database it cannot see (defends against a vanished or never-written file being silently imported).
        // With auto-import selected and a non-existent target, OnRunSucceededAsync's File.Exists guard skips the import.
        ConfigureCreateOutcome(DatabaseToolsOutcome.Succeeded);

        var importRequested = false;
        var component = Render<CreateDatabaseTab>(parameters => parameters
            .AddCascadingValue("RequestAutoImport", (Func<string, bool, Task>)((_, _) =>
            {
                importRequested = true;
                return Task.CompletedTask;
            })));

        // The Succeeded outcome is mocked, so nothing actually writes this path; the random subdirectory keeps it absent.
        var missingDatabasePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.db");
        component.Find("#create-target-path").Input(missingDatabasePath);
        await component.FindAll("[role='option']").Single(option => option.TextContent.Trim() == "Import database").MouseDownAsync(new MouseEventArgs());

        component.Find(".button-green").Click();

        // The run reaches its successful outcome, but the missing artifact suppresses the auto-import.
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll(".outcome-succeeded")));
        Assert.False(importRequested);
    }

    [Fact]
    public async Task DuringManualImport_DisablesRunButton()
    {
        // The manual "Import database" button runs the import with IsRunning=false, but RunCoreAsync still rejects a Run
        // while an import is in progress (guard: _autoImportState == Importing). The Run button must therefore be disabled
        // during a manual import too, so it never presents an actionable control that silently drops the click.
        ConfigureCreateOutcome(DatabaseToolsOutcome.Succeeded);

        var importGate = new TaskCompletionSource();
        var component = Render<CreateDatabaseTab>(parameters => parameters
            .AddCascadingValue("RequestAutoImport", (Func<string, bool, Task>)((_, _) => importGate.Task)));

        component.Find("#create-target-path").Input(@"C:\out.db");

        // Run with auto-import OFF, so the import only happens when the user later clicks "Import database".
        component.Find(".button-green").Click();
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll(".bi-database-add")));

        component.Find(".bi-database-add").ParentElement!.Click();

        // While the manual import is in flight the Run button must be disabled (it would be silently rejected otherwise).
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
        // The post-success auto-import runs while IsRunning is still true (the operation stays non-re-entrant). The
        // leading toolbar must NOT present a run "Cancel" during that window: CancelRun only cancels the already-finished
        // run, so a Cancel shown here could not stop the import it appears to act on. Expect a disabled Run instead.
        // The produced database must exist on disk for the auto-import to fire, so the create mock writes the target.
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

            // While the import is in flight: the trailing slot reports it, and the leading slot offers no actionable
            // Cancel (no DangerButton); only a disabled Run.
            component.WaitForAssertion(() =>
            {
                Assert.Contains("Importing database", component.Markup);
                Assert.Empty(component.FindAll(".button-red"));
                Assert.True(component.Find(".button-green").HasAttribute("disabled"));
            });

            // Releasing the import lets the operation finish: the run-running state clears and the import reports done.
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

        // A .vhdx is an offline image but is read directly from its Windows partition, so no image index applies.
        component.Find("#create-source-path").Input(@"C:\images\disk.vhdx");

        Assert.Empty(component.FindAll("#create-wim-index"));
    }

    [Fact]
    public void IncludeProtectedProviders_ShieldsRunAndRoutesThroughElevatedHelper()
    {
        // Opting to include protected providers must (1) put the UAC shield on Run (the click now elevates) and
        // (2) route the run through the elevated helper, not the in-process service.
        var elevatedRunner = ConfigureElevatedCreateSucceeded();

        var component = Render<CreateDatabaseTab>();
        component.Find("#create-target-path").Input(@"C:\out.db");
        component.Find("#create-include-protected").Change(true);

        Assert.NotEmpty(component.FindAll(".bi-shield-lock"));

        component.Find(".button-green").Click();

        component.WaitForAssertion(() => AssertCreateRoutedThroughElevatedHelper(elevatedRunner));
    }

    [Fact]
    public void LocalScanNonAdmin_OffersProtectedProvidersChoice_RunNotShielded()
    {
        // Default render: empty source (live local providers) on a non-admin process. The fast in-process scan is the
        // default, so Run is NOT shielded (no UAC); the "include protected providers" checkbox is offered to opt in.
        var component = Render<CreateDatabaseTab>();

        Assert.NotNull(component.Find("#create-include-protected"));
        Assert.Empty(component.FindAll(".bi-shield-lock"));
        Assert.NotEmpty(component.FindAll(".bi-play-fill"));
    }

    [Fact]
    public void OfflineImageNonAdmin_ShieldsRun_HidesProtectedProvidersChoice()
    {
        // An offline image always needs admin, so Run auto-elevates (shielded) and the opt-in checkbox is hidden: the
        // choice is meaningless because extraction cannot run without elevation.
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
        // The shield glyph is aria-hidden, so a screen reader's only cue that Run will prompt for elevation is an
        // aria-describedby pointing at a visually-hidden description; it must be present only while the click elevates.
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

    // A temp target path that does not exist yet; the create mock writes it during dispatch, as a real run would.
    private static string BuildTempDatabaseTargetPath() =>
        Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");

    // Deletes any files the create mock wrote during a test (File.Delete is a no-op for an already-absent file).
    private static void DeleteFilesIfPresent(IEnumerable<string> paths)
    {
        foreach (var path in paths) { File.Delete(path); }
    }

    // Asserts the create ran through the elevated helper exactly once and never touched the in-process service.
    private void AssertCreateRoutedThroughElevatedHelper(IElevatedDatabaseToolsRunner elevatedRunner)
    {
        elevatedRunner.ReceivedWithAnyArgs(1).CreateAsync(default!, default!, default, default);
        Services.GetRequiredService<IDatabaseToolsService>()
            .DidNotReceiveWithAnyArgs().CreateAsync(default!, default!, default, default);
    }

    // Drives the in-process create dispatch (the IDatabaseToolsService substitute) to a chosen outcome so a run can be
    // exercised end-to-end. A failed run carries a summary; a successful one does not.
    private void ConfigureCreateOutcome(DatabaseToolsOutcome outcome) =>
        Services.GetRequiredService<IDatabaseToolsService>()
            .CreateAsync(default!, default!, default, default)
            .ReturnsForAnyArgs(Task.FromResult(
                new DatabaseToolsResult(outcome, outcome == DatabaseToolsOutcome.Failed ? "probe blocked" : null, TimeSpan.Zero)));

    // Drives the in-process create dispatch to Succeeded AND writes the produced database to disk, mirroring a real
    // create. OnRunSucceededAsync only auto-imports a produced file File.Exists confirms, and ConfirmBeforeDispatchAsync
    // prompts for overwrite when the target already exists; writing the file during dispatch (not before) satisfies the
    // auto-import guard without tripping the overwrite prompt. Returns the written paths so the caller can delete them.
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

    // Configures the elevated runner's create dispatch to succeed so a run routed through elevation completes.
    private IElevatedDatabaseToolsRunner ConfigureElevatedCreateSucceeded()
    {
        var elevatedRunner = Services.GetRequiredService<IElevatedDatabaseToolsRunner>();
        elevatedRunner.CreateAsync(default!, default!, default, default)
            .ReturnsForAnyArgs(Task.FromResult(new DatabaseToolsResult(DatabaseToolsOutcome.Succeeded, null, TimeSpan.Zero)));
        return elevatedRunner;
    }
}
