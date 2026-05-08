// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;
using EventLogExpert.UI.Services;

namespace EventLogExpert.UI.Tests.Services;

public sealed class MenuServiceTests
{
    [Fact]
    public void Close_ShouldResetCaptureOpenerToDefault()
    {
        // Arrange
        var service = new MenuService();
        service.OpenAt(0, 0, BuildItems(), focusFirst: true, captureOpener: false);

        // Act
        service.Close();

        // Assert — next open defaults to capturing again so a fresh opener is recorded.
        Assert.True(service.ActiveCaptureOpener);
    }

    [Fact]
    public void Close_WhenAlreadyClosed_ShouldNotRaiseStateChanged()
    {
        // Arrange — fresh service is already closed; closing again should be a no-op so
        // listeners (MenuHost, MenuBar) don't react to spurious empty transitions.
        var service = new MenuService();
        var stateChangedCount = 0;
        service.StateChanged += () => stateChangedCount++;

        // Act
        service.Close();

        // Assert
        Assert.Equal(0, stateChangedCount);
        Assert.Null(service.ActiveItems);
        Assert.Equal(0, service.ActiveMenuId);
    }

    [Fact]
    public void Close_WhenOpen_ShouldClearStateAndRaiseStateChanged()
    {
        // Arrange
        var service = new MenuService();
        service.OpenAt(10, 20, BuildItems());

        var stateChangedCount = 0;
        service.StateChanged += () => stateChangedCount++;

        // Act
        service.Close();

        // Assert
        Assert.Equal(1, stateChangedCount);
        Assert.Null(service.ActiveItems);
        Assert.Equal(0, service.ActiveMenuId);
        Assert.Equal(0, service.PositionX);
        Assert.Equal(0, service.PositionY);
        Assert.True(service.ActiveFocusFirst);
    }

    [Fact]
    public void NavigateBar_ShouldForwardDirectionToSubscribers()
    {
        // Arrange — MenuBar subscribes to NavigateBarRequested so the open popup can ask the
        // bar to switch to an adjacent top-level menu (ArrowLeft / ArrowRight inside a popup).
        var service = new MenuService();
        var captured = new List<int>();
        service.NavigateBarRequested += direction => captured.Add(direction);

        // Act
        service.NavigateBar(-1);
        service.NavigateBar(+1);

        // Assert
        Assert.Equal([-1, +1], captured);
    }

    [Fact]
    public void OpenAt_ShouldIncrementMenuIdEachOpen()
    {
        // Arrange — ActiveMenuId is used by MenuHost as a render @key so consecutive opens
        // must produce distinct ids even when they immediately follow each other.
        var service = new MenuService();
        var items = BuildItems();

        // Act
        service.OpenAt(0, 0, items);
        var firstId = service.ActiveMenuId;

        service.OpenAt(0, 0, items);
        var secondId = service.ActiveMenuId;

        service.Close();
        service.OpenAt(0, 0, items);
        var thirdId = service.ActiveMenuId;

        // Assert
        Assert.True(firstId > 0);
        Assert.True(secondId > firstId);
        Assert.True(thirdId > secondId);
    }

    [Fact]
    public void OpenAt_ShouldRaiseStateChangedAndPublishItems()
    {
        // Arrange
        var service = new MenuService();
        var stateChangedCount = 0;
        service.StateChanged += () => stateChangedCount++;
        var items = BuildItems();

        // Act
        service.OpenAt(50, 75, items, focusFirst: false);

        // Assert
        Assert.Equal(1, stateChangedCount);
        Assert.Same(items, service.ActiveItems);
        Assert.Equal(50, service.PositionX);
        Assert.Equal(75, service.PositionY);
        Assert.False(service.ActiveFocusFirst);
        Assert.True(service.ActiveCaptureOpener);
        Assert.True(service.ActiveMenuId > 0);
    }

    [Fact]
    public void OpenAt_WhenCaptureOpenerFalse_ShouldExposeFlagToHost()
    {
        // Arrange — MenuBar passes captureOpener=false during ArrowLeft/ArrowRight bar nav so the
        // host preserves the original menubar opener instead of capturing a transient menu item.
        var service = new MenuService();
        service.OpenAt(0, 0, BuildItems());

        // Act
        service.OpenAt(0, 0, BuildItems(), focusFirst: true, captureOpener: false);

        // Assert
        Assert.False(service.ActiveCaptureOpener);
    }

    [Fact]
    public void OpenAt_WithNullItems_ShouldThrow()
    {
        // Arrange
        var service = new MenuService();

        // Act / Assert — guard against accidental null callsites that would render an empty popup.
        Assert.Throws<ArgumentNullException>(() => service.OpenAt(0, 0, null!));
    }

    private static IReadOnlyList<MenuItem> BuildItems() => [MenuItem.Item("Test", () => { })];
}
