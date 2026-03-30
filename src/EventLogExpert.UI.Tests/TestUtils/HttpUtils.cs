// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Net;
using System.Text;
using System.Text.Json;

namespace EventLogExpert.UI.Tests.TestUtils;

public static class HttpUtils
{
    public sealed class MockHttpMessageHandler(HttpStatusCode statusCode, object? content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(statusCode);

            if (content is not null)
            {
                string jsonContent = content as string ?? JsonSerializer.Serialize(content);

                response.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
            }

            response.RequestMessage = request;

            return Task.FromResult(response);
        }
    }
}
