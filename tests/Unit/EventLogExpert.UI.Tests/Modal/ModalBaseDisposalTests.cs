// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Modal;
using NSubstitute;

namespace EventLogExpert.UI.Tests.Modal;

public sealed class ModalBaseDisposalTests
{
    [Fact]
    public async Task DisposeAsync_RunsTheCoreExactlyOnce()
    {
        var modal = new ProbeModal
        {
            ModalCoordinator = Substitute.For<IModalCoordinator>(),
            ModalService = Substitute.For<IModalService>(),
        };

        await ((IAsyncDisposable)modal).DisposeAsync();
        await ((IAsyncDisposable)modal).DisposeAsync();

        Assert.Equal(1, modal.CoreInvocations);
    }

    [Fact]
    public async Task DisposeAsync_SetsIsDisposedBeforeDerivedDisposeAsyncCoreRuns()
    {
        var modal = new ProbeModal
        {
            ModalCoordinator = Substitute.For<IModalCoordinator>(),
            ModalService = Substitute.For<IModalService>(),
        };

        await ((IAsyncDisposable)modal).DisposeAsync();

        Assert.True(modal.IsDisposedObservedInCore);
    }

    private sealed class ProbeModal : ModalBase<bool>
    {
        public int CoreInvocations { get; private set; }

        public bool IsDisposedObservedInCore { get; private set; }

        protected override ValueTask DisposeAsyncCore(bool disposing)
        {
            CoreInvocations++;
            IsDisposedObservedInCore = IsDisposed;

            return base.DisposeAsyncCore(disposing);
        }
    }
}
