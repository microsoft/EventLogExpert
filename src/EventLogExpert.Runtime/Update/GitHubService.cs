// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using System.Text.Json;

namespace EventLogExpert.Runtime.Update;

internal sealed class GitHubService(HttpClient httpClient, ITraceLogger traceLogger) : IGitHubService
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly ITraceLogger _traceLogger = traceLogger;

    public async Task<IEnumerable<GitHubRelease>> GetReleases()
    {
        var response = await _httpClient.GetAsync("/repos/microsoft/EventLogExpert/releases");

        if (response.IsSuccessStatusCode is not true)
        {
            _traceLogger.Error($"{nameof(GetReleases)} Attempt to retrieve {response.RequestMessage?.RequestUri} failed: {response.StatusCode}.");

            throw new HttpRequestException($"Failed to retrieve GitHub releases. StatusCode: {response.StatusCode}");
        }

        _traceLogger.Debug($"{nameof(GetReleases)} Attempt to retrieve {response.RequestMessage?.RequestUri} succeeded: {response.StatusCode}.");

        var stream = await response.Content.ReadAsStreamAsync();
        var content = await JsonSerializer.DeserializeAsync<IEnumerable<GitHubRelease>>(stream);

        if (content is not null) { return content; }

        _traceLogger.Error($"{nameof(GetReleases)} Failed to deserialize response stream.");
        throw new JsonException($"{nameof(GetReleases)} Failed to deserialize response stream.");
    }
}
