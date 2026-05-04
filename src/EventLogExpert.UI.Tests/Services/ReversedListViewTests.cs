// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Services;

namespace EventLogExpert.UI.Tests.Services;

public sealed class ReversedListViewTests
{
    [Fact]
    public void Add_ShouldThrow()
    {
        // Arrange
        var view = new ReversedListView<string>(new List<string>());

        // Act + Assert
        Assert.Throws<NotSupportedException>(() => view.Add("a"));
    }

    [Fact]
    public void Clear_ShouldThrow()
    {
        // Arrange
        var view = new ReversedListView<string>(new List<string> { "a" });

        // Act + Assert
        Assert.Throws<NotSupportedException>(() => view.Clear());
    }

    [Fact]
    public void Constructor_WhenInnerNull_ShouldThrow()
    {
        // Act + Assert
        Assert.Throws<ArgumentNullException>(() => new ReversedListView<string>(null!));
    }

    [Fact]
    public void Contains_ShouldDelegateToInner()
    {
        // Arrange
        var view = new ReversedListView<string>(new List<string> { "a", "b" });

        // Assert
        Assert.Contains("a", view);
        Assert.DoesNotContain("c", view);
    }

    [Fact]
    public void CopyTo_ShouldWriteInReverseOrder()
    {
        // Arrange
        var view = new ReversedListView<string>(new List<string> { "a", "b", "c" });
        var destination = new string[5];

        // Act
        view.CopyTo(destination, 1);

        // Assert
        Assert.Equal(new[] { null, "c", "b", "a", null }, destination);
    }

    [Fact]
    public void CopyTo_WhenArrayNull_ShouldThrow()
    {
        // Arrange
        var view = new ReversedListView<string>(new List<string> { "a" });

        // Act + Assert
        Assert.Throws<ArgumentNullException>(() => view.CopyTo(null!, 0));
    }

    [Fact]
    public void CopyTo_WhenDestinationTooSmall_ShouldThrow()
    {
        // Arrange
        var view = new ReversedListView<string>(new List<string> { "a", "b", "c" });
        var destination = new string[2];

        // Act + Assert
        Assert.Throws<ArgumentException>(() => view.CopyTo(destination, 0));
    }

    [Fact]
    public void Count_ShouldReflectInnerCount()
    {
        // Arrange
        var inner = new List<int> { 1, 2, 3, 4 };
        var view = new ReversedListView<int>(inner);

        // Act
        inner.Add(5);

        // Assert — view sees mutations to the underlying list (acts as a live view).
        Assert.Equal(5, view.Count);
        Assert.Equal(5, view[0]);
        Assert.Equal(1, view[4]);
    }

    [Fact]
    public void Enumerate_ShouldYieldInReverseOrder()
    {
        // Arrange
        var view = new ReversedListView<string>(new List<string> { "a", "b", "c" });

        // Act
        var result = view.ToList();

        // Assert
        Assert.Equal(new[] { "c", "b", "a" }, result);
    }

    [Fact]
    public void Indexer_ShouldReturnInnerInReverseOrder()
    {
        // Arrange
        var inner = new List<string> { "a", "b", "c" };
        var view = new ReversedListView<string>(inner);

        // Assert
        Assert.Equal("c", view[0]);
        Assert.Equal("b", view[1]);
        Assert.Equal("a", view[2]);
    }

    [Fact]
    public void IndexerSetter_ShouldThrow()
    {
        // Arrange
        var view = new ReversedListView<string>(new List<string> { "a" });

        // Act + Assert
        Assert.Throws<NotSupportedException>(() => ((IList<string>)view)[0] = "z");
    }

    [Fact]
    public void IndexOf_ShouldReturnPositionInReversedView()
    {
        // Arrange
        var view = new ReversedListView<string>(new List<string> { "a", "b", "c" });

        // Act + Assert
        Assert.Equal(0, view.IndexOf("c"));
        Assert.Equal(1, view.IndexOf("b"));
        Assert.Equal(2, view.IndexOf("a"));
        Assert.Equal(-1, view.IndexOf("d"));
    }

    [Fact]
    public void IndexOf_WhenItemAppearsTwice_ShouldReturnFirstOccurrenceInReversedView()
    {
        // Arrange — inner: [a, b, a]; reversed view: [a, b, a]; first occurrence of "a" in reversed view is index 0.
        var view = new ReversedListView<string>(new List<string> { "a", "b", "a" });

        // Act + Assert
        Assert.Equal(0, view.IndexOf("a"));
    }

    [Fact]
    public void Insert_ShouldThrow()
    {
        // Arrange
        var view = new ReversedListView<string>(new List<string> { "a" });

        // Act + Assert
        Assert.Throws<NotSupportedException>(() => view.Insert(0, "z"));
    }

    [Fact]
    public void IsAssignableToIListT_ShouldExposeFastPathInterface()
    {
        // Arrange
        var view = new ReversedListView<string>(new List<string> { "a" });

        // Act + Assert
        Assert.IsAssignableFrom<IList<string>>(view);
    }

    [Fact]
    public void IsReadOnly_ShouldBeTrue()
    {
        // Arrange
        var view = new ReversedListView<string>(new List<string>());

        // Assert
        Assert.True(view.IsReadOnly);
    }

    [Fact]
    public void Remove_ShouldThrow()
    {
        // Arrange
        var view = new ReversedListView<string>(new List<string> { "a" });

        // Act + Assert
        Assert.Throws<NotSupportedException>(() => view.Remove("a"));
    }

    [Fact]
    public void RemoveAt_ShouldThrow()
    {
        // Arrange
        var view = new ReversedListView<string>(new List<string> { "a" });

        // Act + Assert
        Assert.Throws<NotSupportedException>(() => view.RemoveAt(0));
    }
}
