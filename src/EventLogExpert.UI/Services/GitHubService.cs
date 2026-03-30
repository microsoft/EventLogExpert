// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.UI.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace EventLogExpert.UI.Services;

public interface IGitHubService
{
    Task<IEnumerable<GitReleaseModel>> GetReleases();
}

public sealed class GitHubService(HttpClient httpClient, ITraceLogger traceLogger) : IGitHubService
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly ITraceLogger _traceLogger = traceLogger;

    public async Task<IEnumerable<GitReleaseModel>> GetReleases()
    {
        var response = await _httpClient.GetAsync("/repos/microsoft/EventLogExpert/releases");

        if (response.IsSuccessStatusCode is not true)
        {
            _traceLogger.Trace(
                $"{nameof(GetReleases)} Attempt to retrieve {response.RequestMessage?.RequestUri} failed: {response.StatusCode}.",
                LogLevel.Warning);

            throw new HttpRequestException($"Failed to retrieve GitHub releases. StatusCode: {response.StatusCode}");
        }

        _traceLogger.Trace(
            $"{nameof(GetReleases)} Attempt to retrieve {response.RequestMessage?.RequestUri} succeeded: {response.StatusCode}.");

        var stream = await response.Content.ReadAsStreamAsync();
        var content = await JsonSerializer.DeserializeAsync<IEnumerable<GitReleaseModel>>(stream);

        if (content is not null) { return content; }

        _traceLogger.Trace($"{nameof(GetReleases)} Failed to deserialize response stream.", LogLevel.Warning);
        throw new JsonException($"{nameof(GetReleases)} Failed to deserialize response stream.");
    }
}
