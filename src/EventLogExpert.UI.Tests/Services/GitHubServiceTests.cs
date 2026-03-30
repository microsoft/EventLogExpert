// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.UI.Models;
using EventLogExpert.UI.Services;
using EventLogExpert.UI.Tests.TestUtils;
using Microsoft.Extensions.Logging;
using NSubstitute;
using System.Net;
using System.Text.Json;

namespace EventLogExpert.UI.Tests.Services;

public sealed class GitHubServiceTests
{
    [Fact]
    public async Task GetReleases_WhenDeserializationFails_ShouldThrowJsonException()
    {
        // Arrange
        var mockTraceLogger = Substitute.For<ITraceLogger>();
        var mockHttpClient = CreateMockHttpClient(HttpStatusCode.OK, "invalid json");

        var gitHubService = CreateGitHubService(mockHttpClient, mockTraceLogger);

        // Act & Assert
        await Assert.ThrowsAsync<JsonException>(async () => await gitHubService.GetReleases());
    }

    [Fact]
    public async Task GetReleases_WhenHttpRequestFails_ShouldLogWarning()
    {
        // Arrange
        var mockTraceLogger = Substitute.For<ITraceLogger>();
        var mockHttpClient = CreateMockHttpClient(HttpStatusCode.InternalServerError, null);

        var gitHubService = CreateGitHubService(mockHttpClient, mockTraceLogger);

        // Act
        try
        {
            await gitHubService.GetReleases();
        }
        catch (HttpRequestException)
        {
            // Expected
        }

        // Assert
        mockTraceLogger.Received(1)
            .Trace(Arg.Is<string>(s => s.Contains("failed") && s.Contains(nameof(GitHubService.GetReleases))), LogLevel.Warning);
    }

    [Fact]
    public async Task GetReleases_WhenHttpRequestFails_ShouldThrowHttpRequestException()
    {
        // Arrange
        var mockTraceLogger = Substitute.For<ITraceLogger>();
        var mockHttpClient = CreateMockHttpClient(HttpStatusCode.NotFound, null);

        var gitHubService = CreateGitHubService(mockHttpClient, mockTraceLogger);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<HttpRequestException>(async () => await gitHubService.GetReleases());

        Assert.Contains("Failed to retrieve GitHub releases", exception.Message);
        Assert.Contains("NotFound", exception.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task GetReleases_WhenHttpStatusIsNotSuccess_ShouldThrowHttpRequestException(HttpStatusCode statusCode)
    {
        // Arrange
        var mockTraceLogger = Substitute.For<ITraceLogger>();
        var mockHttpClient = CreateMockHttpClient(statusCode, null);

        var gitHubService = CreateGitHubService(mockHttpClient, mockTraceLogger);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<HttpRequestException>(async () => await gitHubService.GetReleases());

        Assert.Contains(statusCode.ToString(), exception.Message);
    }

    [Fact]
    public async Task GetReleases_WhenResponseIsEmptyArray_ShouldReturnEmptyCollection()
    {
        // Arrange
        var mockTraceLogger = Substitute.For<ITraceLogger>();
        var mockHttpClient = CreateMockHttpClient(HttpStatusCode.OK, Array.Empty<GitReleaseModel>());

        var gitHubService = CreateGitHubService(mockHttpClient, mockTraceLogger);

        // Act
        var result = await gitHubService.GetReleases();

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetReleases_WhenResponseIsNull_ShouldLogWarning()
    {
        // Arrange
        var mockTraceLogger = Substitute.For<ITraceLogger>();
        var mockHttpClient = CreateMockHttpClient(HttpStatusCode.OK, "null");

        var gitHubService = CreateGitHubService(mockHttpClient, mockTraceLogger);

        // Act
        try
        {
            await gitHubService.GetReleases();
        }
        catch (JsonException)
        {
            // Expected
        }

        // Assert
        mockTraceLogger.Received(1)
            .Trace(Arg.Is<string>(s => s.Contains("Failed to deserialize")), LogLevel.Warning);
    }

    [Fact]
    public async Task GetReleases_WhenResponseIsNull_ShouldThrowJsonException()
    {
        // Arrange
        var mockTraceLogger = Substitute.For<ITraceLogger>();
        var mockHttpClient = CreateMockHttpClient(HttpStatusCode.OK, "null");

        var gitHubService = CreateGitHubService(mockHttpClient, mockTraceLogger);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<JsonException>(async () => await gitHubService.GetReleases());

        Assert.Contains("Failed to deserialize response stream", exception.Message);
    }

    [Fact]
    public async Task GetReleases_WhenSuccessful_ShouldLogSuccess()
    {
        // Arrange
        var mockTraceLogger = Substitute.For<ITraceLogger>();
        var mockHttpClient = CreateMockHttpClient(HttpStatusCode.OK, GitHubUtils.CreateGitReleaseModels());

        var gitHubService = CreateGitHubService(mockHttpClient, mockTraceLogger);

        // Act
        await gitHubService.GetReleases();

        // Assert
        mockTraceLogger.Received(1).Trace(
            Arg.Is<string>(s => s.Contains("succeeded") && s.Contains(nameof(GitHubService.GetReleases))),
            Arg.Any<LogLevel>());
    }

    [Fact]
    public async Task GetReleases_WhenSuccessful_ShouldReturnContent()
    {
        // Arrange
        var mockTraceLogger = Substitute.For<ITraceLogger>();
        var mockHttpClient = CreateMockHttpClient(HttpStatusCode.OK, GitHubUtils.CreateGitReleaseModels());

        var gitHubService = CreateGitHubService(mockHttpClient, mockTraceLogger);

        // Act
        var content = await gitHubService.GetReleases();

        // Assert
        Assert.NotNull(content);
        Assert.NotEmpty(content);
    }

    [Fact]
    public async Task GetReleases_WhenSuccessful_ShouldReturnValidReleaseModels()
    {
        // Arrange
        var mockTraceLogger = Substitute.For<ITraceLogger>();
        var mockHttpClient = CreateMockHttpClient(HttpStatusCode.OK, GitHubUtils.CreateGitReleaseModels());

        var gitHubService = CreateGitHubService(mockHttpClient, mockTraceLogger);

        // Act
        var result = await gitHubService.GetReleases();

        // Assert
        Assert.All(result,
            release =>
            {
                Assert.NotNull(release.Version);
                Assert.NotNull(release.Assets);
            });
    }

    private static GitHubService CreateGitHubService(HttpClient httpClient, ITraceLogger? traceLogger = null) =>
        new(httpClient, traceLogger ?? Substitute.For<ITraceLogger>());

    private static HttpClient CreateMockHttpClient(HttpStatusCode statusCode, object? content)
    {
        HttpUtils.MockHttpMessageHandler handler = new(statusCode, content);
        HttpClient client = new(handler) { BaseAddress = new Uri("https://api.github.com/") };

        return client;
    }
}
