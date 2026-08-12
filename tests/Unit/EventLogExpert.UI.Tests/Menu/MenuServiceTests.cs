// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Menu;

namespace EventLogExpert.UI.Tests.Menu;

public sealed class MenuServiceTests
{
    [Fact]
    public void Close_ShouldResetCaptureOpenerToDefault()
    {
        var service = new MenuService();
        service.OpenAt(0, 0, BuildItems(), true, false);

        service.Close();

        Assert.True(service.ActiveCaptureOpener);
    }

    [Fact]
    public void Close_WhenAlreadyClosed_ShouldNotRaiseStateChanged()
    {
        var service = new MenuService();
        var stateChangedCount = 0;
        service.StateChanged += () => stateChangedCount++;

        service.Close();

        Assert.Equal(0, stateChangedCount);
        Assert.Null(service.ActiveItems);
        Assert.Equal(0, service.ActiveMenuId);
    }

    [Fact]
    public void Close_WhenOpen_ShouldClearStateAndRaiseStateChanged()
    {
        var service = new MenuService();
        service.OpenAt(10, 20, BuildItems());

        var stateChangedCount = 0;
        service.StateChanged += () => stateChangedCount++;

        service.Close();

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
        var service = new MenuService();
        var captured = new List<int>();
        service.NavigateBarRequested += direction => captured.Add(direction);

        service.NavigateBar(-1);
        service.NavigateBar(+1);

        Assert.Equal([-1, +1], captured);
    }

    [Fact]
    public void OpenAt_ShouldIncrementMenuIdEachOpen()
    {
        var service = new MenuService();
        var items = BuildItems();

        service.OpenAt(0, 0, items);
        var firstId = service.ActiveMenuId;

        service.OpenAt(0, 0, items);
        var secondId = service.ActiveMenuId;

        service.Close();
        service.OpenAt(0, 0, items);
        var thirdId = service.ActiveMenuId;

        Assert.True(firstId > 0);
        Assert.True(secondId > firstId);
        Assert.True(thirdId > secondId);
    }

    [Fact]
    public void OpenAt_ShouldRaiseStateChangedAndPublishItems()
    {
        var service = new MenuService();
        var stateChangedCount = 0;
        service.StateChanged += () => stateChangedCount++;
        var items = BuildItems();

        service.OpenAt(50, 75, items, false);

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
        var service = new MenuService();
        service.OpenAt(0, 0, BuildItems());

        service.OpenAt(0, 0, BuildItems(), true, false);

        Assert.False(service.ActiveCaptureOpener);
    }

    [Fact]
    public void OpenAt_WithNullItems_ShouldThrow()
    {
        var service = new MenuService();

        Assert.Throws<ArgumentNullException>(() => service.OpenAt(0, 0, null!));
    }

    private static IReadOnlyList<MenuItem> BuildItems() => [MenuItem.Item("Test", () => { })];
}
