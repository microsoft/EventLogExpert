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

public class GitHubService : IGitHubService
{
    private readonly ITraceLogger _traceLogger;

    public GitHubService(ITraceLogger traceLogger)
    {
        _traceLogger = traceLogger;
    }

    public async Task<IEnumerable<GitReleaseModel>> GetReleases()
    {
        HttpClient client = new() { BaseAddress = new Uri("https://api.github.com/"), };

        client.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        client.DefaultRequestHeaders.UserAgent.TryParseAdd("request");

        var response = await client.GetAsync("/repos/microsoft/EventLogExpert/releases");

        if (response.IsSuccessStatusCode is not true)
        {
            _traceLogger.Trace($"{nameof(GetReleases)} Attempt to retrieve {response.RequestMessage?.RequestUri} failed: {response.StatusCode}.", LogLevel.Warning);
            throw new Exception($"Failed to retrieve GitHub releases. StatusCode: {response.StatusCode}");
        }

        _traceLogger.Trace($"{nameof(GetReleases)} Attempt to retrieve {response.RequestMessage?.RequestUri} succeeded: {response.StatusCode}.", LogLevel.Warning);

        var stream = await response.Content.ReadAsStreamAsync();
        var content = await JsonSerializer.DeserializeAsync<IEnumerable<GitReleaseModel>>(stream);

        if (content is null)
        {
            _traceLogger.Trace($"{nameof(GetReleases)} Failed to deserialize response stream.", LogLevel.Warning);
            throw new Exception($"{nameof(GetReleases)} Failed to deserialize response stream.");
        }

        return content;
    }
}
