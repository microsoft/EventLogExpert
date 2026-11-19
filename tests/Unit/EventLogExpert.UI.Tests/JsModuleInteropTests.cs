// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Common.Interop;
using Microsoft.JSInterop;
using Microsoft.JSInterop.Infrastructure;
using NSubstitute;
using NSubstitute.Extensions;

namespace EventLogExpert.UI.Tests;

public sealed class JsModuleInteropTests
{
    public static TheoryData<Exception> StrictDisposeSwallowedExceptions() =>
    [
        new JSDisconnectedException("circuit gone"),
        new ObjectDisposedException("module"),
        new TaskCanceledException(),
        new OperationCanceledException()
    ];

    public static TheoryData<Exception> SwallowedTeardownExceptions() =>
    [
        new JSDisconnectedException("circuit gone"),
        new JSException("module call failed"),
        new ObjectDisposedException("module"),
        new TaskCanceledException()
    ];

    public static TheoryData<Exception> UnwrappedInvocationExceptions() =>
    [
        new JSDisconnectedException("disconnected"),
        new OperationCanceledException(),
        new TaskCanceledException(),
        new ObjectDisposedException("invocation"),
        new InvalidOperationException("unexpected invocation failure")
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

    [Fact]
    public async Task DisposeModuleStrictAsync_DisposesTheModule()
    {
        var module = Substitute.For<IJSObjectReference>();

        await JsModuleInterop.DisposeModuleStrictAsync(module);

        await module.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task DisposeModuleStrictAsync_NullModule_DoesNotThrow()
    {
        await JsModuleInterop.DisposeModuleStrictAsync(null);
    }

    [Fact]
    public async Task DisposeModuleStrictAsync_PropagatesJsException()
    {
        var module = Substitute.For<IJSObjectReference>();
        var failure = new JSException("module call failed");
        module.DisposeAsync().Returns(ValueTask.FromException(failure));

        var exception = await Assert.ThrowsAsync<JSException>(
            async () => await JsModuleInterop.DisposeModuleStrictAsync(module));

        Assert.Same(failure, exception);
    }

    [Fact]
    public async Task DisposeModuleStrictAsync_PropagatesUnexpectedException()
    {
        var module = Substitute.For<IJSObjectReference>();
        module.DisposeAsync().Returns(ValueTask.FromException(new InvalidOperationException("unexpected")));

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await JsModuleInterop.DisposeModuleStrictAsync(module));
    }

    [Theory]
    [MemberData(nameof(StrictDisposeSwallowedExceptions))]
    public async Task DisposeModuleStrictAsync_SwallowsExpectedDisposeException(Exception teardownException)
    {
        var module = Substitute.For<IJSObjectReference>();
        module.DisposeAsync().Returns(ValueTask.FromException(teardownException));

        await JsModuleInterop.DisposeModuleStrictAsync(module);

        await module.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task ImportModuleAsync_FaultedImport_ReportsModuleAndOriginalCause()
    {
        var runtime = Substitute.For<IJSRuntime>();
        var originalException = new JSException("The module could not be loaded.");
        var modulePath = "./broken-import.js";
        runtime.ReturnsForAll(ValueTask.FromException<IJSObjectReference>(originalException));

        var exception = await Assert.ThrowsAsync<JSException>(
            async () => await JsModuleInterop.ImportModuleAsync(runtime, modulePath));

        Assert.Equal($"Failed to import JavaScript module '{modulePath}': {originalException.Message}", exception.Message);
        Assert.Same(originalException, exception.InnerException);
    }

    [Theory]
    [MemberData(nameof(UnwrappedInvocationExceptions))]
    public async Task ImportModuleAsync_FaultedNonJsFailure_PreservesOriginalException(Exception originalException)
    {
        var runtime = Substitute.For<IJSRuntime>();
        runtime.ReturnsForAll(ValueTask.FromException<IJSObjectReference>(originalException));

        var exception = await Record.ExceptionAsync(
            async () => await JsModuleInterop.ImportModuleAsync(runtime, "./faulted-module.js"));

        Assert.Same(originalException, exception);
    }

    [Fact]
    public void ImportModuleAsync_ImmediateJsFailure_ReportsModuleWithoutDeferringFailure()
    {
        var runtime = Substitute.For<IJSRuntime>();
        var originalException = new JSException("Import could not start.");
        var modulePath = "./immediate-import.js";
        runtime.ReturnsForAll<ValueTask<IJSObjectReference>>(_ => throw originalException);

        var exception = Assert.Throws<JSException>(() =>
        {
            _ = JsModuleInterop.ImportModuleAsync(runtime, modulePath);
        });

        Assert.Equal($"Failed to import JavaScript module '{modulePath}': {originalException.Message}", exception.Message);
        Assert.Same(originalException, exception.InnerException);
    }

    [Theory]
    [MemberData(nameof(UnwrappedInvocationExceptions))]
    public void ImportModuleAsync_ImmediateNonJsFailure_PreservesImmediateException(Exception originalException)
    {
        var runtime = Substitute.For<IJSRuntime>();
        runtime.ReturnsForAll<ValueTask<IJSObjectReference>>(_ => throw originalException);

        var exception = Record.Exception(() =>
        {
            _ = JsModuleInterop.ImportModuleAsync(runtime, "./immediate-failure.js");
        });

        Assert.Same(originalException, exception);
    }

    [Fact]
    public async Task ImportModuleAsync_Success_ForwardsPathAndReturnsImportedModule()
    {
        var runtime = Substitute.For<IJSRuntime>();
        var module = Substitute.For<IJSObjectReference>();
        var modulePath = "./successful-import.js";
        runtime.ReturnsForAll(ValueTask.FromResult(module));

        var importedModule = await JsModuleInterop.ImportModuleAsync(runtime, modulePath);

        Assert.Same(module, importedModule);
        var invocation = Assert.Single(runtime.ReceivedCalls());
        Assert.Equal(nameof(IJSRuntime.InvokeAsync), invocation.GetMethodInfo().Name);
        Assert.Equal("import", invocation.GetArguments()[0]);
        Assert.Equal([modulePath], Assert.IsType<object?[]>(invocation.GetArguments()[1]));
    }

    [Fact]
    public async Task InvokeAsync_FaultedJsFailure_ReportsInvocationAndOriginalCause()
    {
        var module = Substitute.For<IJSObjectReference>();
        var originalException = new JSException("The selected text is unavailable.");
        var identifier = "getSelectedText";
        module.ReturnsForAll(ValueTask.FromException<string>(originalException));

        var exception = await Assert.ThrowsAsync<JSException>(
            async () => await JsModuleInterop.InvokeAsync<string>(module, identifier));

        Assert.Equal($"JavaScript invocation '{identifier}' failed: {originalException.Message}", exception.Message);
        Assert.Same(originalException, exception.InnerException);
    }

    [Theory]
    [MemberData(nameof(UnwrappedInvocationExceptions))]
    public async Task InvokeAsync_FaultedNonJsFailure_PreservesOriginalException(Exception originalException)
    {
        var module = Substitute.For<IJSObjectReference>();
        module.ReturnsForAll(ValueTask.FromException<int>(originalException));

        var exception = await Record.ExceptionAsync(
            async () => await JsModuleInterop.InvokeAsync<int>(module, "getPendingCount"));

        Assert.Same(originalException, exception);
    }

    [Fact]
    public void InvokeAsync_ImmediateJsFailure_ReportsInvocationWithoutDeferringFailure()
    {
        var module = Substitute.For<IJSObjectReference>();
        var originalException = new JSException("The row count is unavailable.");
        var identifier = "getVisibleRowCount";
        module.ReturnsForAll<ValueTask<int>>(_ => throw originalException);

        var exception = Assert.Throws<JSException>(() =>
        {
            _ = JsModuleInterop.InvokeAsync<int>(module, identifier);
        });

        Assert.Equal($"JavaScript invocation '{identifier}' failed: {originalException.Message}", exception.Message);
        Assert.Same(originalException, exception.InnerException);
    }

    [Theory]
    [MemberData(nameof(UnwrappedInvocationExceptions))]
    public void InvokeAsync_ImmediateNonJsFailure_PreservesImmediateException(Exception originalException)
    {
        var module = Substitute.For<IJSObjectReference>();
        module.ReturnsForAll<ValueTask<int>>(_ => throw originalException);

        var exception = Record.Exception(() =>
        {
            _ = JsModuleInterop.InvokeAsync<int>(module, "getCurrentCount");
        });

        Assert.Same(originalException, exception);
    }

    [Fact]
    public async Task InvokeAsync_Success_ForwardsArgumentsAndReturnsTypedResult()
    {
        var module = Substitute.For<IJSObjectReference>();
        var identifier = "findRow";
        object?[] arguments = ["row-key", 17, null];
        module.ReturnsForAll(ValueTask.FromResult(23));

        var result = await JsModuleInterop.InvokeAsync<int>(module, identifier, arguments);

        Assert.Equal(23, result);
        var invocation = Assert.Single(module.ReceivedCalls());
        Assert.Equal(nameof(IJSObjectReference.InvokeAsync), invocation.GetMethodInfo().Name);
        Assert.Equal(identifier, invocation.GetArguments()[0]);
        Assert.Same(arguments, invocation.GetArguments()[1]);
    }

    [Fact]
    public async Task InvokeVoidAsync_FaultedInvocation_ReportsInvocationAndOriginalCause()
    {
        var module = Substitute.For<IJSObjectReference>();
        var originalException = new JSException("The listener could not be registered.");
        var identifier = "registerListener";
        module.ReturnsForAll(ValueTask.FromException<IJSVoidResult>(originalException));

        var exception = await Assert.ThrowsAsync<JSException>(
            async () => await JsModuleInterop.InvokeVoidAsync(module, identifier));

        Assert.Equal($"JavaScript invocation '{identifier}' failed: {originalException.Message}", exception.Message);
        Assert.Same(originalException, exception.InnerException);
    }

    [Theory]
    [MemberData(nameof(UnwrappedInvocationExceptions))]
    public async Task InvokeVoidAsync_FaultedNonJsFailure_PreservesOriginalException(Exception originalException)
    {
        var module = Substitute.For<IJSObjectReference>();
        module.ReturnsForAll(ValueTask.FromException<IJSVoidResult>(originalException));

        var exception = await Record.ExceptionAsync(
            async () => await JsModuleInterop.InvokeVoidAsync(module, "completePendingAction"));

        Assert.Same(originalException, exception);
    }

    [Fact]
    public async Task InvokeVoidAsync_ImmediateJsFailure_RetainsAwaitTimeFailureAndContext()
    {
        var module = Substitute.For<IJSObjectReference>();
        var originalException = new JSException("Focus could not be restored.");
        var identifier = "restoreFocus";
        module.ReturnsForAll<ValueTask<IJSVoidResult>>(_ => throw originalException);

        var invocation = JsModuleInterop.InvokeVoidAsync(module, identifier);
        var exception = await Assert.ThrowsAsync<JSException>(async () => await invocation);

        Assert.Equal($"JavaScript invocation '{identifier}' failed: {originalException.Message}", exception.Message);
        Assert.Same(originalException, exception.InnerException);
    }

    [Theory]
    [MemberData(nameof(UnwrappedInvocationExceptions))]
    public async Task InvokeVoidAsync_ImmediateNonJsFailure_RetainsAwaitTimeFailure(Exception originalException)
    {
        var module = Substitute.For<IJSObjectReference>();
        module.ReturnsForAll<ValueTask<IJSVoidResult>>(_ => throw originalException);

        var invocation = JsModuleInterop.InvokeVoidAsync(module, "retireCurrentAction");
        var exception = await Record.ExceptionAsync(async () => await invocation);

        Assert.Same(originalException, exception);
    }

    [Fact]
    public async Task InvokeVoidAsync_Success_ForwardsArgumentsAndAwaitsCompletion()
    {
        var module = Substitute.For<IJSObjectReference>();
        var identifier = "focusRow";
        object?[] arguments = [null, 31];
        TaskCompletionSource<IJSVoidResult> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        module.ReturnsForAll(new ValueTask<IJSVoidResult>(completion.Task));

        var invocation = JsModuleInterop.InvokeVoidAsync(module, identifier, arguments);

        Assert.False(invocation.IsCompleted);
        var receivedCall = Assert.Single(module.ReceivedCalls());
        Assert.Equal(nameof(IJSObjectReference.InvokeAsync), receivedCall.GetMethodInfo().Name);
        Assert.Equal(identifier, receivedCall.GetArguments()[0]);
        Assert.Same(arguments, receivedCall.GetArguments()[1]);
        completion.SetResult(Substitute.For<IJSVoidResult>());
        await invocation;
    }
}
