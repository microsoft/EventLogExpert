// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.IntegrationTests.TestUtils.Constants;
using EventLogExpert.Eventing.Logging;
using EventLogExpert.Eventing.Providers;
using EventLogExpert.Eventing.Readers;
using Microsoft.Win32;
using NSubstitute;

namespace EventLogExpert.Eventing.IntegrationTests.Providers;

public sealed class RegistryProviderTests
{
    private static readonly string[] s_commonLegacyProviderNames =
    [
        Constants.ApplicationLogName,
        Constants.SystemLogName,
        Constants.KernelGeneralLogName,
        Constants.PowerShellLogName,
        Constants.SecurityAuditingLogName,
        Constants.ServiceControlManagerLogName
    ];

    [Fact]
    public void Constructor_WhenCalledWithNullLogger_ShouldNotThrow()
    {
        // Act & Assert
        var provider = new RegistryProvider();
        Assert.NotNull(provider);
    }

    [Fact]
    public void Constructor_WhenCreatedMultipleTimes_ShouldCreateDifferentInstances()
    {
        // Arrange & Act
        var provider1 = new RegistryProvider();
        var provider2 = new RegistryProvider();

        // Assert
        Assert.NotNull(provider1);
        Assert.NotNull(provider2);
        Assert.NotSame(provider1, provider2);
    }

    [Fact]
    public void GetMessageFilesForLegacyProvider_WhenCalled_ShouldNotIncludeSysFiles()
    {
        // Arrange
        var provider = new RegistryProvider();

        // Act
        var result = FindAnyLegacyProviderFiles(provider);

        // Assert
        Assert.NotEmpty(result);

        Assert.All(result,
            path =>
            {
                var extension = Path.GetExtension(path).ToLower();
                Assert.NotEqual(".sys", extension);
            });
    }

    [Fact]
    public void GetMessageFilesForLegacyProvider_WhenCalledWithLogger_ShouldLogTrace()
    {
        // Arrange
        var providerName = "TestProvider_" + Guid.NewGuid();
        var mockLogger = Substitute.For<ITraceLogger>();
        var provider = new RegistryProvider(mockLogger);

        // Act
        _ = provider.GetMessageFilesForLegacyProvider(providerName).ToList();

        // Assert
        mockLogger.Received(1).Debug(
            Arg.Is<DebugLogHandler>(h =>
                h.ToString().Contains("GetMessageFilesForLegacyProvider called") &&
                h.ToString().Contains(providerName)));
    }

    [Fact]
    public void GetMessageFilesForLegacyProvider_WhenCalledWithoutLogger_ShouldNotThrow()
    {
        // Arrange
        var provider = new RegistryProvider();

        // Act
        var result = provider.GetMessageFilesForLegacyProvider(Constants.ApplicationLogName);

        // Assert
        Assert.NotNull(result);
    }

    [Theory]
    [InlineData(Constants.ApplicationLogName)]
    [InlineData(Constants.SystemLogName)]
    public void GetMessageFilesForLegacyProvider_WhenCommonLogNames_ShouldNotThrow(string providerName)
    {
        // Arrange
        var provider = new RegistryProvider();

        // Act
        var result = provider.GetMessageFilesForLegacyProvider(providerName);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public void GetMessageFilesForLegacyProvider_WhenCommonWindowsProvider_ShouldReturnFiles()
    {
        // Arrange
        var provider = new RegistryProvider();

        // Act
        var result = FindAnyLegacyProviderFiles(provider);

        // Assert
        Assert.NotEmpty(result);
    }

    [Fact]
    public void GetMessageFilesForLegacyProvider_WhenEmptyProviderName_ShouldReturnEmpty()
    {
        // Arrange
        var provider = new RegistryProvider();

        // Act
        var result = provider.GetMessageFilesForLegacyProvider(string.Empty).ToList();

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void GetMessageFilesForLegacyProvider_WhenInvalidProviderName_ShouldReturnEmpty()
    {
        // Arrange
        var providerName = "NonExistentProvider_" + Guid.NewGuid();
        var provider = new RegistryProvider();

        // Act
        var result = provider.GetMessageFilesForLegacyProvider(providerName).ToList();

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void GetMessageFilesForLegacyProvider_WhenLocalComputer_ShouldExpandEnvironmentVariables()
    {
        // Arrange
        var provider = new RegistryProvider();

        // Act
        var result = FindAnyLegacyProviderFiles(provider);

        // Assert
        Assert.NotEmpty(result);

        Assert.All(result,
            path =>
            {
                Assert.DoesNotContain("%", path);
            });
    }

    [Fact]
    public void GetMessageFilesForLegacyProvider_WhenMultipleMessageFilesExist_ShouldReturnAll()
    {
        // Arrange
        var provider = new RegistryProvider();

        // Act
        foreach (var providerName in s_commonLegacyProviderNames)
        {
            var result = provider.GetMessageFilesForLegacyProvider(providerName).ToList();

            if (result.Count > 1)
            {
                // Assert
                Assert.All(result, file => Assert.False(string.IsNullOrWhiteSpace(file)));
                return;
            }
        }

        Assert.Skip("No common legacy provider on this host has multiple message files registered.");
    }

    [Fact]
    public async Task GetMessageFilesForLegacyProvider_WhenMultipleProviders_ShouldHandleConcurrentAccess()
    {
        // Arrange
        var provider = new RegistryProvider();

        var providerNames = new[]
            { Constants.ApplicationLogName, Constants.SystemLogName, Constants.KernelGeneralLogName };

        // Act
        var tasks = providerNames.Select(name =>
            Task.Run(() => provider.GetMessageFilesForLegacyProvider(name).ToList())
        ).ToArray();

        await Task.WhenAll(tasks);

        // Assert
        Assert.All(tasks,
            task =>
            {
                Assert.NotNull(task.Result);
                Assert.True(task.IsCompletedSuccessfully);
            });
    }

    [Fact]
    public void GetMessageFilesForLegacyProvider_WhenMultipleSemicolonSeparatedPaths_ShouldReturnMultipleFiles()
    {
        // Arrange
        var provider = new RegistryProvider();

        // Act
        var (providerName, files) = FindLegacyProviderWithSemicolonSplitFiles(provider);

        // Assert — environment-dependent; not every host registers such a provider.
        Assert.SkipUnless(providerName is not null,
            "Test requires a legacy provider with semicolon-separated EventMessageFile entries on this host.");

        Assert.True(files.Count > 1,
            $"Expected multiple files after semicolon split for provider '{providerName}', got {files.Count}.");

        Assert.All(files, file => Assert.False(string.IsNullOrWhiteSpace(file)));
    }

    [Fact]
    public void GetMessageFilesForLegacyProvider_WhenNoSubkeyFound_ShouldLogTerminalMessage()
    {
        // Arrange
        var providerName = "DefinitelyMissingProvider_" + Guid.NewGuid();
        var mockLogger = Substitute.For<ITraceLogger>();
        var provider = new RegistryProvider(mockLogger);

        // Act
        _ = provider.GetMessageFilesForLegacyProvider(providerName).ToList();

        // Assert
        mockLogger.Received().Debug(
            Arg.Is<DebugLogHandler>(h =>
                h.ToString().Contains("No legacy EventMessageFile found for provider") &&
                h.ToString().Contains(providerName)));
    }

    [Fact]
    public void GetMessageFilesForLegacyProvider_WhenNullComputerName_ShouldUseLocalMachine()
    {
        // Arrange
        var provider = new RegistryProvider();

        // Act
        var result = provider.GetMessageFilesForLegacyProvider(Constants.ApplicationLogName);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public void GetMessageFilesForLegacyProvider_WhenProviderExists_ShouldReturnEnumerable()
    {
        // Arrange
        var provider = new RegistryProvider();

        // Act
        var result = provider.GetMessageFilesForLegacyProvider("Application");

        // Assert
        Assert.NotNull(result);
        Assert.IsAssignableFrom<IEnumerable<string>>(result);
    }

    [Fact]
    public void GetMessageFilesForLegacyProvider_WhenProviderFound_ShouldLogFoundMessage()
    {
        // Arrange — discover a known-good provider name first so the mock-logger assertion binds to it.
        var probe = new RegistryProvider();
        var providerName = FindAnyLegacyProviderName(probe);
        Assert.NotNull(providerName);

        var mockLogger = Substitute.For<ITraceLogger>();
        var provider = new RegistryProvider(mockLogger);

        // Act
        var foundFiles = provider.GetMessageFilesForLegacyProvider(providerName).ToList();

        // Assert — bind the "Found message file" debug emission to the specific
        // provider name we asked for so a stray emission cannot mask a regression.
        Assert.NotEmpty(foundFiles);

        mockLogger.Received()
            .Debug(Arg.Is<DebugLogHandler>(h =>
                h.ToString().Contains("Found message file for legacy provider") &&
                h.ToString().Contains(providerName)));
    }

    [Fact]
    public void GetMessageFilesForLegacyProvider_WhenReturnedPaths_ShouldBeDllOrExe()
    {
        // Arrange
        var provider = new RegistryProvider();

        // Act
        var result = FindAnyLegacyProviderFiles(provider);

        // Assert
        Assert.NotEmpty(result);

        Assert.All(result,
            path =>
            {
                var extension = Path.GetExtension(path).ToLower();

                Assert.True(extension is ".dll" or ".exe",
                    $"Expected .dll or .exe, but got {extension} for path: {path}");
            });
    }

    [Fact]
    public void GetMessageFilesForLegacyProvider_WhenReturnedPaths_ShouldBeFullPaths()
    {
        // Arrange
        var provider = new RegistryProvider();

        // Act
        var result = FindAnyLegacyProviderFiles(provider);

        // Assert
        Assert.NotEmpty(result);

        Assert.All(result,
            path =>
            {
                Assert.True(
                    Path.IsPathFullyQualified(path) || path.StartsWith(@"\\"),
                    $"Expected fully qualified path, but got: {path}");
            });
    }

    [Fact]
    public void GetMessageFilesForLegacyProvider_WhenReturnedPaths_ShouldNotBeEmpty()
    {
        // Arrange
        var provider = new RegistryProvider();

        // Act
        var result = FindAnyLegacyProviderFiles(provider);

        // Assert
        Assert.NotEmpty(result);

        Assert.All(result,
            path =>
            {
                Assert.False(string.IsNullOrWhiteSpace(path));
            });
    }

    [Fact]
    public void GetMessageFilesForLegacyProvider_WhenReturnedPaths_ShouldNotContainDuplicates()
    {
        // Arrange
        var provider = new RegistryProvider();

        // Act
        var result = FindAnyLegacyProviderFiles(provider);

        // Assert
        Assert.NotEmpty(result);
        var uniquePaths = result.Distinct().Count();
        Assert.Equal(result.Count, uniquePaths);
    }

    [Fact]
    public void GetMessageFilesForLegacyProvider_WhenSameProviderMultipleTimes_ShouldReturnConsistentResults()
    {
        // Arrange — probe for a known-good provider name (Application/System are
        // log names, not provider names — the SUT returns empty for them).
        var provider = new RegistryProvider();
        var providerName = FindAnyLegacyProviderName(provider);
        Assert.NotNull(providerName);

        // Act
        var result1 = provider.GetMessageFilesForLegacyProvider(providerName).ToList();
        var result2 = provider.GetMessageFilesForLegacyProvider(providerName).ToList();

        // Assert
        Assert.NotEmpty(result1);
        Assert.Equal(result1.Count, result2.Count);

        for (int i = 0; i < result1.Count; i++)
        {
            Assert.Equal(result1[i], result2[i]);
        }
    }

    [Fact]
    public void GetMessageFilesForLegacyProvider_WhenSpecialCharactersInProviderName_ShouldHandleGracefully()
    {
        // Arrange
        var providerName = "Invalid<>Provider|Name";
        var provider = new RegistryProvider();

        // Act
        var result = provider.GetMessageFilesForLegacyProvider(providerName).ToList();

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void GetMessageFilesForLegacyProvider_WhenWhitespaceProviderName_ShouldReturnEmpty()
    {
        // Arrange
        var provider = new RegistryProvider();

        // Act
        var result = provider.GetMessageFilesForLegacyProvider("   ").ToList();

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    private static List<string> FindAnyLegacyProviderFiles(RegistryProvider provider)
    {
        foreach (var providerName in s_commonLegacyProviderNames)
        {
            var result = provider.GetMessageFilesForLegacyProvider(providerName).ToList();

            if (result.Count != 0)
            {
                return result;
            }
        }

        return [];
    }

    private static string? FindAnyLegacyProviderName(RegistryProvider provider)
    {
        foreach (var providerName in s_commonLegacyProviderNames)
        {
            if (provider.GetMessageFilesForLegacyProvider(providerName).Any())
            {
                return providerName;
            }
        }

        return null;
    }

    private static (string? Name, List<string> Files) FindLegacyProviderWithSemicolonSplitFiles(
        RegistryProvider provider)
    {
        using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default);
        using var eventLogKey = hklm.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\EventLog");

        if (eventLogKey is null)
        {
            return (null, []);
        }

        // Mirrors SUT: returns from the first log subtree where a provider has a non-empty EMF.
        var seenWithEmf = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var logName in eventLogKey.GetSubKeyNames())
        {
            if (LogNames.AdminOnlyLiveLogNames.Contains(logName))
            {
                continue;
            }

            using var logSubKey = eventLogKey.OpenSubKey(logName);

            if (logSubKey is null)
            {
                continue;
            }

            foreach (var providerName in logSubKey.GetSubKeyNames())
            {
                using var providerSubKey = logSubKey.OpenSubKey(providerName);

                if (providerSubKey?.GetValue("EventMessageFile") is not string emf || string.IsNullOrEmpty(emf))
                {
                    continue;
                }

                // First non-empty EMF wins per provider name across log subtrees.
                if (!seenWithEmf.Add(providerName))
                {
                    continue;
                }

                if (!emf.Contains(';'))
                {
                    continue;
                }

                var files = provider.GetMessageFilesForLegacyProvider(providerName).ToList();

                if (files.Count > 1)
                {
                    return (providerName, files);
                }
            }
        }

        return (null, []);
    }
}
