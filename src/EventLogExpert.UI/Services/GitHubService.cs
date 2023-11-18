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

public sealed class GitHubService(ITraceLogger traceLogger) : IGitHubService
{
    public async Task<IEnumerable<GitReleaseModel>> GetReleases()
    {
        HttpClient client = new() { BaseAddress = new Uri("https://api.github.com/"), };

        client.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        client.DefaultRequestHeaders.UserAgent.TryParseAdd("request");

        var response = await client.GetAsync("/repos/microsoft/EventLogExpert/releases");

        if (response.IsSuccessStatusCode is not true)
        {
            traceLogger.Trace($"{nameof(GetReleases)} Attempt to retrieve {response.RequestMessage?.RequestUri} failed: {response.StatusCode}.", LogLevel.Warning);
            throw new Exception($"Failed to retrieve GitHub releases. StatusCode: {response.StatusCode}");
        }

        traceLogger.Trace($"{nameof(GetReleases)} Attempt to retrieve {response.RequestMessage?.RequestUri} succeeded: {response.StatusCode}.", LogLevel.Warning);

        var stream = await response.Content.ReadAsStreamAsync();
        var content = await JsonSerializer.DeserializeAsync<IEnumerable<GitReleaseModel>>(stream);

        if (content is not null) { return content; }

        traceLogger.Trace($"{nameof(GetReleases)} Failed to deserialize response stream.", LogLevel.Warning);
        throw new Exception($"{nameof(GetReleases)} Failed to deserialize response stream.");

    }
}
