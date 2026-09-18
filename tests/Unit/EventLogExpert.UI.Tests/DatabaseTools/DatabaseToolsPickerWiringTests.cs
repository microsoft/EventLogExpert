// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using AngleSharp.Dom;
using Bunit;
using EventLogExpert.Runtime.Common.Files;
using EventLogExpert.Runtime.DatabaseTools;
using EventLogExpert.UI.DatabaseTools.Tabs;
using EventLogExpert.UI.Tests.TestUtils;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace EventLogExpert.UI.Tests.DatabaseTools;

public sealed class DatabaseToolsPickerWiringTests : BunitContext
{
    private const string ExistingDatabaseInitialDirectory = "initial-existing-database";
    private const string OutputInitialDirectory = "initial-output";
    private const string PickedOpenPath = @"picked\open.db";
    private const string PickedSavePath = @"picked\save.db";
    private const string PlaceholderExistingDatabase = "[[DatabaseTools_PathInput_Placeholder_ExistingDatabase]]";
    private const string PlaceholderOutput = "[[DatabaseTools_PathInput_Placeholder_Output]]";
    private const string PlaceholderSource = "[[DatabaseTools_PathInput_Placeholder_Source]]";
    private const string SourceInitialDirectory = "initial-source";

    private readonly IFilePickerService _filePicker;
    private readonly IDatabaseToolsPickerDirectory _pickerDirectory;

    public DatabaseToolsPickerWiringTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddDatabaseToolsTabDependencies();
        _filePicker = Services.GetRequiredService<IFilePickerService>();
        _pickerDirectory = Services.GetRequiredService<IDatabaseToolsPickerDirectory>();
        _pickerDirectory.ResolveInitialDirectoryAsync(DatabaseToolsPickRole.Source).Returns(SourceInitialDirectory);
        _pickerDirectory.ResolveInitialDirectoryAsync(DatabaseToolsPickRole.Output).Returns(OutputInitialDirectory);
        _pickerDirectory.ResolveInitialDirectoryAsync(DatabaseToolsPickRole.ExistingDatabase).Returns(ExistingDatabaseInitialDirectory);
        _filePicker.PickAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<string?>()).Returns(PickedOpenPath);
        _filePicker.PickSaveAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<string?>(), Arg.Any<string?>()).Returns(PickedSavePath);
    }

    [Fact]
    public async Task BrowseButtons_ResolveInitialDirectoryByPickerRole_AndPassItToFilePicker()
    {
        await AssertSavePickerInitialDirectoryAsync<CreateDatabaseTab>(
            "create-target-path",
            "[[DatabaseTools_Picker_OutputDbNewName]]",
            DatabaseToolsPickRole.Output,
            OutputInitialDirectory);
        await AssertOpenPickerInitialDirectoryAsync<CreateDatabaseTab>(
            "create-source-path",
            "[[Db_Create_Picker_Source]]",
            DatabaseToolsPickRole.Source,
            SourceInitialDirectory);
        await AssertOpenPickerInitialDirectoryAsync<CreateDatabaseTab>(
            "create-skip-path",
            "[[Db_Create_Picker_SkipSource]]",
            DatabaseToolsPickRole.Source,
            SourceInitialDirectory);
        await AssertOpenPickerInitialDirectoryAsync<DiffDatabasesTab>(
            "diff-first-path",
            "[[Db_Diff_Picker_FirstSource]]",
            DatabaseToolsPickRole.Source,
            SourceInitialDirectory);
        await AssertOpenPickerInitialDirectoryAsync<DiffDatabasesTab>(
            "diff-second-path",
            "[[Db_Diff_Picker_SecondSource]]",
            DatabaseToolsPickRole.Source,
            SourceInitialDirectory);
        await AssertSavePickerInitialDirectoryAsync<DiffDatabasesTab>(
            "diff-new-db",
            "[[DatabaseTools_Picker_OutputDbNewName]]",
            DatabaseToolsPickRole.Output,
            OutputInitialDirectory);
        await AssertOpenPickerInitialDirectoryAsync<MergeDatabaseTab>(
            "merge-source-path",
            "[[DatabaseTools_Picker_SourceDbOrEvtx]]",
            DatabaseToolsPickRole.Source,
            SourceInitialDirectory);
        await AssertOpenPickerInitialDirectoryAsync<MergeDatabaseTab>(
            "merge-target-path",
            "[[Db_Merge_Picker_TargetDb]]",
            DatabaseToolsPickRole.ExistingDatabase,
            ExistingDatabaseInitialDirectory);
        await AssertOpenPickerInitialDirectoryAsync<ShowProvidersTab>(
            "show-source-path",
            "[[DatabaseTools_Picker_SourceDbOrEvtx]]",
            DatabaseToolsPickRole.Source,
            SourceInitialDirectory);
        await AssertOpenPickerInitialDirectoryAsync<UpgradeDatabaseTab>(
            "upgrade-db-path",
            "[[Db_Upgrade_Picker_Database]]",
            DatabaseToolsPickRole.ExistingDatabase,
            ExistingDatabaseInitialDirectory);
    }

    [Fact]
    public void DatabaseToolsPathInputs_RoutePlaceholdersThroughRoleSpecificLocalizedPathPlaceholders()
    {
        IRenderedComponent<CreateDatabaseTab> create = Render<CreateDatabaseTab>();
        IRenderedComponent<DiffDatabasesTab> diff = Render<DiffDatabasesTab>();
        IRenderedComponent<MergeDatabaseTab> merge = Render<MergeDatabaseTab>();
        IRenderedComponent<ShowProvidersTab> show = Render<ShowProvidersTab>();
        IRenderedComponent<UpgradeDatabaseTab> upgrade = Render<UpgradeDatabaseTab>();

        Assert.Equal(PlaceholderOutput, create.Find("#create-target-path").GetAttribute("placeholder"));
        Assert.Equal(PlaceholderSource, create.Find("#create-source-path").GetAttribute("placeholder"));
        Assert.Equal(PlaceholderSource, create.Find("#create-skip-path").GetAttribute("placeholder"));
        Assert.Equal(PlaceholderSource, diff.Find("#diff-first-path").GetAttribute("placeholder"));
        Assert.Equal(PlaceholderSource, diff.Find("#diff-second-path").GetAttribute("placeholder"));
        Assert.Equal(PlaceholderOutput, diff.Find("#diff-new-db").GetAttribute("placeholder"));
        Assert.Equal(PlaceholderSource, merge.Find("#merge-source-path").GetAttribute("placeholder"));
        Assert.Equal(PlaceholderExistingDatabase, merge.Find("#merge-target-path").GetAttribute("placeholder"));
        Assert.Equal(PlaceholderSource, show.Find("#show-source-path").GetAttribute("placeholder"));
        Assert.Equal(PlaceholderExistingDatabase, upgrade.Find("#upgrade-db-path").GetAttribute("placeholder"));
    }

    private static async Task ClickBrowseButtonAsync<TComponent>(IRenderedComponent<TComponent> component, string inputId)
        where TComponent : IComponent
    {
        IElement input = component.Find($"#{inputId}");
        IElement group = input.ParentElement ?? throw new InvalidOperationException("Path input must be inside a path input group.");
        IElement button = group.QuerySelector("button") ?? throw new InvalidOperationException("Path input group must include a browse button.");

        await button.ClickAsync(new MouseEventArgs());
    }

    private async Task AssertOpenPickerInitialDirectoryAsync<TComponent>(
        string inputId,
        string pickerTitle,
        DatabaseToolsPickRole role,
        string initialDirectory)
        where TComponent : IComponent
    {
        IRenderedComponent<TComponent> component = Render<TComponent>();

        await ClickBrowseButtonAsync(component, inputId);

        await _pickerDirectory.Received(1).ResolveInitialDirectoryAsync(role);
        await _filePicker.Received(1).PickAsync(pickerTitle, Arg.Any<IReadOnlyList<string>>(), initialDirectory);
        _pickerDirectory.Received(1).RememberDirectory(role, PickedOpenPath);
        ClearPickerCalls();
    }

    private async Task AssertSavePickerInitialDirectoryAsync<TComponent>(
        string inputId,
        string pickerTitle,
        DatabaseToolsPickRole role,
        string initialDirectory)
        where TComponent : IComponent
    {
        IRenderedComponent<TComponent> component = Render<TComponent>();

        await ClickBrowseButtonAsync(component, inputId);

        await _pickerDirectory.Received(1).ResolveInitialDirectoryAsync(role);
        await _filePicker.Received(1).PickSaveAsync(pickerTitle, Arg.Any<IReadOnlyList<string>>(), Arg.Any<string?>(), initialDirectory);
        _pickerDirectory.Received(1).RememberDirectory(role, PickedSavePath);
        ClearPickerCalls();
    }

    private void ClearPickerCalls()
    {
        _filePicker.ClearReceivedCalls();
        _pickerDirectory.ClearReceivedCalls();
    }
}
