// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.IO.Pipes;

namespace EventLogExpert.Runtime.Tests.DatabaseTools.Elevation.TestUtils;

internal sealed class HelperPipePair : IAsyncDisposable
{
    public required NamedPipeClientStream Client { get; init; }

    public required NamedPipeServerStream Server { get; init; }

    public static async Task<HelperPipePair> CreateAsync(CancellationToken cancellationToken)
    {
        var pipeName = $"elt-runner-test-{Guid.NewGuid():N}";

        var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        var client = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        var serverConnect = server.WaitForConnectionAsync(cancellationToken);
        var clientConnect = client.ConnectAsync(cancellationToken);

        try
        {
            await Task.WhenAll(serverConnect, clientConnect);
        }
        catch
        {
            await server.DisposeAsync();
            await client.DisposeAsync();
            throw;
        }

        return new HelperPipePair { Server = server, Client = client };
    }

    public async ValueTask DisposeAsync()
    {
        try { await Server.DisposeAsync(); } catch { /* idempotent dispose */ }
        try { await Client.DisposeAsync(); } catch { /* idempotent dispose */ }
    }
}
