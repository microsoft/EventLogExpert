// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.DatabaseTools;
using NSubstitute;

namespace EventLogExpert.Runtime.Tests.DatabaseTools;

public sealed class DatabaseToolsPickerDirectoryTests
{
    [Fact]
    public void RememberDirectory_WhenPickIsCancelled_DoesNotPersistDirectory()
    {
        IDatabaseToolsPickerPreferencesProvider preferencesProvider = Substitute.For<IDatabaseToolsPickerPreferencesProvider>();
        IDatabaseToolsFallbackDirectoryProvider fallbackDirectoryProvider = Substitute.For<IDatabaseToolsFallbackDirectoryProvider>();
        IDirectoryExistence directoryExistence = Substitute.For<IDirectoryExistence>();
        DatabaseToolsPickerDirectory pickerDirectory = new(preferencesProvider, fallbackDirectoryProvider, directoryExistence);

        pickerDirectory.RememberDirectory(DatabaseToolsPickRole.Source, null);

        preferencesProvider.DidNotReceive().SetLastDirectory(Arg.Any<DatabaseToolsPickRole>(), Arg.Any<string>());
    }

    [Fact]
    public void RememberDirectory_WhenPickedPathHasDirectory_PersistsDirectoryForRole()
    {
        string pickedPath = Path.Combine("picked", "source.db");
        string expectedDirectory = Path.GetDirectoryName(pickedPath) ?? throw new InvalidOperationException("Test path must include a directory.");
        IDatabaseToolsPickerPreferencesProvider preferencesProvider = Substitute.For<IDatabaseToolsPickerPreferencesProvider>();
        IDatabaseToolsFallbackDirectoryProvider fallbackDirectoryProvider = Substitute.For<IDatabaseToolsFallbackDirectoryProvider>();
        IDirectoryExistence directoryExistence = Substitute.For<IDirectoryExistence>();
        DatabaseToolsPickerDirectory pickerDirectory = new(preferencesProvider, fallbackDirectoryProvider, directoryExistence);

        pickerDirectory.RememberDirectory(DatabaseToolsPickRole.Source, pickedPath);

        preferencesProvider.Received(1).SetLastDirectory(DatabaseToolsPickRole.Source, expectedDirectory);
    }

    [Theory]
    [InlineData(DatabaseToolsPickRole.Source, "fallback-source")]
    [InlineData(DatabaseToolsPickRole.Output, "fallback-output")]
    [InlineData(DatabaseToolsPickRole.ExistingDatabase, "fallback-existing-database")]
    public async Task ResolveInitialDirectoryAsync_ForEachRole_AsksFallbackProviderForThatRole(
        DatabaseToolsPickRole role,
        string fallbackDirectory)
    {
        IDatabaseToolsPickerPreferencesProvider preferencesProvider = Substitute.For<IDatabaseToolsPickerPreferencesProvider>();
        IDatabaseToolsFallbackDirectoryProvider fallbackDirectoryProvider = Substitute.For<IDatabaseToolsFallbackDirectoryProvider>();
        IDirectoryExistence directoryExistence = Substitute.For<IDirectoryExistence>();
        preferencesProvider.GetLastDirectory(role).Returns((string?)null);
        fallbackDirectoryProvider.GetFallbackDirectories(role).Returns(new[] { fallbackDirectory });
        directoryExistence.ExistsAsync(fallbackDirectory).Returns(true);
        DatabaseToolsPickerDirectory pickerDirectory = new(preferencesProvider, fallbackDirectoryProvider, directoryExistence);

        string? initialDirectory = await pickerDirectory.ResolveInitialDirectoryAsync(role);

        Assert.Equal(fallbackDirectory, initialDirectory);
        fallbackDirectoryProvider.Received(1).GetFallbackDirectories(role);
    }

    [Fact]
    public async Task ResolveInitialDirectoryAsync_WhenFallbackDirectoryIsMissing_ReturnsNoInitialDirectory()
    {
        const string fallbackDirectory = "fallback-output";
        IDatabaseToolsPickerPreferencesProvider preferencesProvider = Substitute.For<IDatabaseToolsPickerPreferencesProvider>();
        IDatabaseToolsFallbackDirectoryProvider fallbackDirectoryProvider = Substitute.For<IDatabaseToolsFallbackDirectoryProvider>();
        IDirectoryExistence directoryExistence = Substitute.For<IDirectoryExistence>();
        preferencesProvider.GetLastDirectory(DatabaseToolsPickRole.Output).Returns((string?)null);
        fallbackDirectoryProvider.GetFallbackDirectories(DatabaseToolsPickRole.Output).Returns(new[] { fallbackDirectory });
        directoryExistence.ExistsAsync(fallbackDirectory).Returns(false);
        DatabaseToolsPickerDirectory pickerDirectory = new(preferencesProvider, fallbackDirectoryProvider, directoryExistence);

        string? initialDirectory = await pickerDirectory.ResolveInitialDirectoryAsync(DatabaseToolsPickRole.Output);

        Assert.Null(initialDirectory);
        await directoryExistence.Received(1).ExistsAsync(fallbackDirectory);
    }

    [Fact]
    public async Task ResolveInitialDirectoryAsync_WhenFirstFallbackCandidateIsMissing_ProbesNextCandidateInOrder()
    {
        const string missingDownloads = "missing-downloads";
        const string existingDocuments = "existing-documents";
        IDatabaseToolsPickerPreferencesProvider preferencesProvider = Substitute.For<IDatabaseToolsPickerPreferencesProvider>();
        IDatabaseToolsFallbackDirectoryProvider fallbackDirectoryProvider = Substitute.For<IDatabaseToolsFallbackDirectoryProvider>();
        IDirectoryExistence directoryExistence = Substitute.For<IDirectoryExistence>();
        preferencesProvider.GetLastDirectory(DatabaseToolsPickRole.Source).Returns((string?)null);
        fallbackDirectoryProvider.GetFallbackDirectories(DatabaseToolsPickRole.Source).Returns(new[] { missingDownloads, existingDocuments });
        directoryExistence.ExistsAsync(missingDownloads).Returns(false);
        directoryExistence.ExistsAsync(existingDocuments).Returns(true);
        DatabaseToolsPickerDirectory pickerDirectory = new(preferencesProvider, fallbackDirectoryProvider, directoryExistence);

        string? initialDirectory = await pickerDirectory.ResolveInitialDirectoryAsync(DatabaseToolsPickRole.Source);

        Assert.Equal(existingDocuments, initialDirectory);
        await directoryExistence.Received(1).ExistsAsync(missingDownloads);
        await directoryExistence.Received(1).ExistsAsync(existingDocuments);
    }

    [Fact]
    public async Task ResolveInitialDirectoryAsync_WhenNoFallbackCandidateExists_ReturnsNoInitialDirectory()
    {
        const string missingDownloads = "missing-downloads";
        const string missingDocuments = "missing-documents";
        IDatabaseToolsPickerPreferencesProvider preferencesProvider = Substitute.For<IDatabaseToolsPickerPreferencesProvider>();
        IDatabaseToolsFallbackDirectoryProvider fallbackDirectoryProvider = Substitute.For<IDatabaseToolsFallbackDirectoryProvider>();
        IDirectoryExistence directoryExistence = Substitute.For<IDirectoryExistence>();
        preferencesProvider.GetLastDirectory(DatabaseToolsPickRole.Source).Returns((string?)null);
        fallbackDirectoryProvider.GetFallbackDirectories(DatabaseToolsPickRole.Source).Returns(new[] { missingDownloads, missingDocuments });
        directoryExistence.ExistsAsync(missingDownloads).Returns(false);
        directoryExistence.ExistsAsync(missingDocuments).Returns(false);
        DatabaseToolsPickerDirectory pickerDirectory = new(preferencesProvider, fallbackDirectoryProvider, directoryExistence);

        string? initialDirectory = await pickerDirectory.ResolveInitialDirectoryAsync(DatabaseToolsPickRole.Source);

        Assert.Null(initialDirectory);
        await directoryExistence.Received(1).ExistsAsync(missingDownloads);
        await directoryExistence.Received(1).ExistsAsync(missingDocuments);
    }

    [Fact]
    public async Task ResolveInitialDirectoryAsync_WhenPersistedAndFallbackDirectoriesAreUnset_ReturnsNoInitialDirectory()
    {
        IDatabaseToolsPickerPreferencesProvider preferencesProvider = Substitute.For<IDatabaseToolsPickerPreferencesProvider>();
        IDatabaseToolsFallbackDirectoryProvider fallbackDirectoryProvider = Substitute.For<IDatabaseToolsFallbackDirectoryProvider>();
        IDirectoryExistence directoryExistence = Substitute.For<IDirectoryExistence>();
        preferencesProvider.GetLastDirectory(DatabaseToolsPickRole.Source).Returns((string?)null);
        fallbackDirectoryProvider.GetFallbackDirectories(DatabaseToolsPickRole.Source).Returns(Array.Empty<string>());
        DatabaseToolsPickerDirectory pickerDirectory = new(preferencesProvider, fallbackDirectoryProvider, directoryExistence);

        string? initialDirectory = await pickerDirectory.ResolveInitialDirectoryAsync(DatabaseToolsPickRole.Source);

        Assert.Null(initialDirectory);
        await directoryExistence.DidNotReceive().ExistsAsync(Arg.Any<string>());
    }

    [Fact]
    public async Task ResolveInitialDirectoryAsync_WhenPersistedDirectoryExists_UsesPersistedDirectory()
    {
        const string persistedDirectory = "persisted-source";
        IDatabaseToolsPickerPreferencesProvider preferencesProvider = Substitute.For<IDatabaseToolsPickerPreferencesProvider>();
        IDatabaseToolsFallbackDirectoryProvider fallbackDirectoryProvider = Substitute.For<IDatabaseToolsFallbackDirectoryProvider>();
        IDirectoryExistence directoryExistence = Substitute.For<IDirectoryExistence>();
        preferencesProvider.GetLastDirectory(DatabaseToolsPickRole.Source).Returns(persistedDirectory);
        directoryExistence.ExistsAsync(persistedDirectory).Returns(true);
        DatabaseToolsPickerDirectory pickerDirectory = new(preferencesProvider, fallbackDirectoryProvider, directoryExistence);

        string? initialDirectory = await pickerDirectory.ResolveInitialDirectoryAsync(DatabaseToolsPickRole.Source);

        Assert.Equal(persistedDirectory, initialDirectory);
        await directoryExistence.Received(1).ExistsAsync(persistedDirectory);
        fallbackDirectoryProvider.DidNotReceive().GetFallbackDirectories(DatabaseToolsPickRole.Source);
    }

    [Fact]
    public async Task ResolveInitialDirectoryAsync_WhenPersistedDirectoryIsMissing_UsesExistingFallbackDirectory()
    {
        const string persistedDirectory = "persisted-source";
        const string fallbackDirectory = "fallback-source";
        IDatabaseToolsPickerPreferencesProvider preferencesProvider = Substitute.For<IDatabaseToolsPickerPreferencesProvider>();
        IDatabaseToolsFallbackDirectoryProvider fallbackDirectoryProvider = Substitute.For<IDatabaseToolsFallbackDirectoryProvider>();
        IDirectoryExistence directoryExistence = Substitute.For<IDirectoryExistence>();
        preferencesProvider.GetLastDirectory(DatabaseToolsPickRole.Source).Returns(persistedDirectory);
        fallbackDirectoryProvider.GetFallbackDirectories(DatabaseToolsPickRole.Source).Returns(new[] { fallbackDirectory });
        directoryExistence.ExistsAsync(persistedDirectory).Returns(false);
        directoryExistence.ExistsAsync(fallbackDirectory).Returns(true);
        DatabaseToolsPickerDirectory pickerDirectory = new(preferencesProvider, fallbackDirectoryProvider, directoryExistence);

        string? initialDirectory = await pickerDirectory.ResolveInitialDirectoryAsync(DatabaseToolsPickRole.Source);

        Assert.Equal(fallbackDirectory, initialDirectory);
        await directoryExistence.Received(1).ExistsAsync(persistedDirectory);
        await directoryExistence.Received(1).ExistsAsync(fallbackDirectory);
    }

    [Fact]
    public async Task ResolveInitialDirectoryAsync_WhenPersistedDirectoryIsUnset_UsesExistingFallbackDirectory()
    {
        const string fallbackDirectory = "fallback-existing-database";
        IDatabaseToolsPickerPreferencesProvider preferencesProvider = Substitute.For<IDatabaseToolsPickerPreferencesProvider>();
        IDatabaseToolsFallbackDirectoryProvider fallbackDirectoryProvider = Substitute.For<IDatabaseToolsFallbackDirectoryProvider>();
        IDirectoryExistence directoryExistence = Substitute.For<IDirectoryExistence>();
        preferencesProvider.GetLastDirectory(DatabaseToolsPickRole.ExistingDatabase).Returns((string?)null);
        fallbackDirectoryProvider.GetFallbackDirectories(DatabaseToolsPickRole.ExistingDatabase).Returns(new[] { fallbackDirectory });
        directoryExistence.ExistsAsync(fallbackDirectory).Returns(true);
        DatabaseToolsPickerDirectory pickerDirectory = new(preferencesProvider, fallbackDirectoryProvider, directoryExistence);

        string? initialDirectory = await pickerDirectory.ResolveInitialDirectoryAsync(DatabaseToolsPickRole.ExistingDatabase);

        Assert.Equal(fallbackDirectory, initialDirectory);
        await directoryExistence.Received(1).ExistsAsync(fallbackDirectory);
    }
}
