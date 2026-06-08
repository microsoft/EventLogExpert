// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Common.Interop;
using Microsoft.JSInterop;
using NSubstitute;

namespace EventLogExpert.UI.Tests;

public sealed class JsModuleInteropTests
{
    public static TheoryData<Exception> SwallowedTeardownExceptions() =>
    [
        new JSDisconnectedException("circuit gone"),
        new JSException("module call failed"),
        new ObjectDisposedException("module"),
        new TaskCanceledException()
    ];

    [Fact]
    public async Task DisposeModuleSafelyAsync_DisposesTheModule()
    {
        var module = Substitute.For<IJSObjectReference>();

        await JsModuleInterop.DisposeModuleSafelyAsync(module);

        await module.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task DisposeModuleSafelyAsync_NullModule_DoesNotThrow()
    {
        await JsModuleInterop.DisposeModuleSafelyAsync(null);
    }

    [Fact]
    public async Task DisposeModuleSafelyAsync_PreDisposeThrowsExpected_SkipsDisposeAndSwallows()
    {
        var module = Substitute.For<IJSObjectReference>();

        await JsModuleInterop.DisposeModuleSafelyAsync(
            module,
            _ => ValueTask.FromException(new JSDisconnectedException("circuit gone")));

        await module.DidNotReceive().DisposeAsync();
    }

    [Fact]
    public async Task DisposeModuleSafelyAsync_PreDisposeThrowsUnexpected_Propagates()
    {
        var module = Substitute.For<IJSObjectReference>();

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await JsModuleInterop.DisposeModuleSafelyAsync(
                module,
                _ => ValueTask.FromException(new InvalidOperationException("unexpected"))));

        await module.DidNotReceive().DisposeAsync();
    }

    [Fact]
    public async Task DisposeModuleSafelyAsync_PropagatesUnexpectedException()
    {
        var module = Substitute.For<IJSObjectReference>();
        module.DisposeAsync().Returns(ValueTask.FromException(new InvalidOperationException("unexpected")));

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await JsModuleInterop.DisposeModuleSafelyAsync(module));
    }

    [Fact]
    public async Task DisposeModuleSafelyAsync_RunsPreDisposeBeforeDispose()
    {
        var module = Substitute.For<IJSObjectReference>();
        var calls = new List<string>();
        module.When(m => m.DisposeAsync()).Do(_ => calls.Add("dispose"));

        await JsModuleInterop.DisposeModuleSafelyAsync(module, _ =>
        {
            calls.Add("preDispose");

            return ValueTask.CompletedTask;
        });

        Assert.Equal(["preDispose", "dispose"], calls);
    }

    [Theory]
    [MemberData(nameof(SwallowedTeardownExceptions))]
    public async Task DisposeModuleSafelyAsync_SwallowsExpectedDisposeException(Exception teardownException)
    {
        var module = Substitute.For<IJSObjectReference>();
        module.DisposeAsync().Returns(ValueTask.FromException(teardownException));

        await JsModuleInterop.DisposeModuleSafelyAsync(module);
    }
}
