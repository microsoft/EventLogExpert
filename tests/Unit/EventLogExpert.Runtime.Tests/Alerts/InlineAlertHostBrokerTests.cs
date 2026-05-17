// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.Modal;
using NSubstitute;

namespace EventLogExpert.Runtime.Tests.Alerts;

public sealed class InlineAlertHostBrokerTests
{
    [Fact]
    public void Register_AfterUnregister_OverwritesPriorState()
    {
        // Arrange — same modal id reuses the broker after an explicit Unregister.
        var modalService = Substitute.For<IModalService>();
        modalService.ActiveModalId.Returns(5L);
        var broker = new InlineAlertHostBroker(modalService);
        broker.Register(5L, new FakeInlineAlertHost { Tag = "first" });
        broker.Unregister(5L);

        var second = new FakeInlineAlertHost { Tag = "second" };

        // Act
        broker.Register(5L, second);

        // Assert
        Assert.True(broker.TryGet(out var resolved));
        Assert.Same(second, resolved);
    }

    [Fact]
    public void Register_NullHost_Throws()
    {
        // Arrange
        var modalService = Substitute.For<IModalService>();
        modalService.ActiveModalId.Returns(1L);
        var broker = new InlineAlertHostBroker(modalService);

        // Act + Assert
        Assert.Throws<ArgumentNullException>(() => broker.Register(1L, null!));
    }

    [Fact]
    public void Register_WithCurrentId_ExposesHost()
    {
        // Arrange
        var modalService = Substitute.For<IModalService>();
        modalService.ActiveModalId.Returns(7L);
        var broker = new InlineAlertHostBroker(modalService);
        var host = new FakeInlineAlertHost();

        // Act
        broker.Register(7L, host);

        // Assert
        Assert.True(broker.TryGet(out var resolved));
        Assert.Same(host, resolved);
    }

    [Fact]
    public void Register_WithStaleId_IsNoOp()
    {
        // Arrange — modal A was active when registration was queued, but modal B is active by the time the call lands.
        var modalService = Substitute.For<IModalService>();
        modalService.ActiveModalId.Returns(2L); // current = B
        var broker = new InlineAlertHostBroker(modalService);
        var staleHost = new FakeInlineAlertHost();

        // Act
        broker.Register(1L, staleHost); // A's late registration

        // Assert — broker does not adopt the stale host.
        Assert.False(broker.TryGet(out var resolved));
        Assert.Null(resolved);
    }

    [Fact]
    public void TryGet_AfterActiveModalChanged_LazilyInvalidatesStaleHost()
    {
        // Arrange — modal A registered while active. Modal B has since replaced A but A has not yet
        // unregistered (Dispose hasn't fired). The broker must not surface A's host to alert dispatchers.
        var modalService = Substitute.For<IModalService>();
        modalService.ActiveModalId.Returns(1L);
        var broker = new InlineAlertHostBroker(modalService);
        broker.Register(1L, new FakeInlineAlertHost());
        Assert.True(broker.TryGet(out _));

        modalService.ActiveModalId.Returns(2L); // B replaced A.

        // Act
        var found = broker.TryGet(out var resolved);

        // Assert
        Assert.False(found);
        Assert.Null(resolved);
    }

    [Fact]
    public void TryGet_WhenNoHostRegistered_ReturnsFalse()
    {
        // Arrange
        var modalService = Substitute.For<IModalService>();
        modalService.ActiveModalId.Returns(0L);
        var broker = new InlineAlertHostBroker(modalService);

        // Act
        var found = broker.TryGet(out var resolved);

        // Assert
        Assert.False(found);
        Assert.Null(resolved);
    }

    [Fact]
    public void Unregister_WithCurrentId_ClearsHost()
    {
        // Arrange
        var modalService = Substitute.For<IModalService>();
        modalService.ActiveModalId.Returns(3L);
        var broker = new InlineAlertHostBroker(modalService);
        broker.Register(3L, new FakeInlineAlertHost());

        // Act
        broker.Unregister(3L);

        // Assert
        Assert.False(broker.TryGet(out var resolved));
        Assert.Null(resolved);
    }

    [Fact]
    public void Unregister_WithStaleId_DoesNotClearCurrentHost()
    {
        // Arrange — A registered, then B replaced A and registered. A's late Dispose fires Unregister(A.id).
        // B's host must remain intact.
        var modalService = Substitute.For<IModalService>();
        modalService.ActiveModalId.Returns(10L);
        var broker = new InlineAlertHostBroker(modalService);
        broker.Register(10L, new FakeInlineAlertHost { Tag = "A" });

        modalService.ActiveModalId.Returns(11L);
        var bHost = new FakeInlineAlertHost { Tag = "B" };
        broker.Register(11L, bHost);

        // Act — A's stale Unregister.
        broker.Unregister(10L);

        // Assert
        Assert.True(broker.TryGet(out var resolved));
        Assert.Same(bHost, resolved);
    }

    private sealed class FakeInlineAlertHost : IInlineAlertHost
    {
        public string? Tag { get; set; }

        public Task<InlineAlertResult> ShowInlineAlertAsync(InlineAlertRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new InlineAlertResult(true, null));
    }
}
