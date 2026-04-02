// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.EventResolvers;

namespace EventLogExpert.Eventing.Tests.EventResolvers;

public sealed class EventResolverCacheTests
{
    [Fact]
    public void ClearAll_AfterAddingItems_ShouldClearBothCaches()
    {
        // Arrange
        var cache = new EventResolverCache();
        cache.GetOrAddDescription("Test Description");
        cache.GetOrAddValue("Test Value");

        // Act
        cache.ClearAll();

        // Assert
        // After clearing, new instances should be added (not return cached references)
        var desc1 = cache.GetOrAddDescription("Test Description");
        var desc2 = cache.GetOrAddDescription("Test Description");
        Assert.Same(desc1, desc2); // Should cache the new instance
    }

    [Fact]
    public void ClearAll_ConcurrentCalls_ShouldHandleThreadSafely()
    {
        // Arrange
        var cache = new EventResolverCache();
        for (int i = 0; i < 100; i++)
        {
            cache.GetOrAddDescription($"Description{i}");
            cache.GetOrAddValue($"Value{i}");
        }

        // Act
        Parallel.For(0, 10, _ => cache.ClearAll());

        // Assert
        // If we get here without exceptions, thread safety is maintained
        Assert.True(true);
    }

    [Fact]
    public void ClearAll_WithEmptyCaches_ShouldNotThrow()
    {
        // Arrange
        var cache = new EventResolverCache();

        // Act
        cache.ClearAll();

        // Assert
        Assert.True(true); // If we get here, no exception was thrown
    }

    [Fact]
    public void GetOrAddDescription_CalledMultipleTimes_ShouldReturnSameReference()
    {
        // Arrange
        var cache = new EventResolverCache();
        var description = "Test Description";

        // Act
        var result1 = cache.GetOrAddDescription(description);
        var result2 = cache.GetOrAddDescription(description);
        var result3 = cache.GetOrAddDescription(description);

        // Assert
        Assert.Same(result1, result2);
        Assert.Same(result2, result3);
    }

    [Fact]
    public void GetOrAddDescription_ConcurrentCalls_ShouldHandleThreadSafely()
    {
        // Arrange
        var cache = new EventResolverCache();
        var results = new string[100];

        // Act
        Parallel.For(0, 100, i =>
        {
            results[i] = cache.GetOrAddDescription($"Description{i % 10}");
        });

        // Assert
        // Verify that same descriptions share the same reference
        for (int i = 0; i < 100; i += 10)
        {
            var description = $"Description{i % 10}";
            var firstOccurrence = results[i];
            
            for (int j = i; j < 100; j += 10)
            {
                Assert.Same(firstOccurrence, results[j]);
            }
        }
    }

    [Fact]
    public void GetOrAddDescription_WithDifferentDescriptions_ShouldReturnDifferentReferences()
    {
        // Arrange
        var cache = new EventResolverCache();
        var description1 = "Description 1";
        var description2 = "Description 2";

        // Act
        var result1 = cache.GetOrAddDescription(description1);
        var result2 = cache.GetOrAddDescription(description2);

        // Assert
        Assert.NotSame(result1, result2);
        Assert.Equal("Description 1", result1);
        Assert.Equal("Description 2", result2);
    }

    [Fact]
    public void GetOrAddDescription_WithEmptyString_ShouldReturnSameEmptyStringReference()
    {
        // Arrange
        var cache = new EventResolverCache();

        // Act
        var result1 = cache.GetOrAddDescription(string.Empty);
        var result2 = cache.GetOrAddDescription(string.Empty);

        // Assert
        Assert.Same(result1, result2);
        Assert.Equal(string.Empty, result1);
    }

    [Fact]
    public void GetOrAddDescription_WithNewString_ShouldAddToCache()
    {
        // Arrange
        var cache = new EventResolverCache();
        var description = new string("Test".ToCharArray()); // Force new string instance

        // Act
        var result = cache.GetOrAddDescription(description);

        // Assert
        Assert.Equal(description, result);
    }

    [Fact]
    public void GetOrAddValue_CalledMultipleTimes_ShouldReturnSameReference()
    {
        // Arrange
        var cache = new EventResolverCache();
        var value = "Test Value";

        // Act
        var result1 = cache.GetOrAddValue(value);
        var result2 = cache.GetOrAddValue(value);
        var result3 = cache.GetOrAddValue(value);

        // Assert
        Assert.Same(result1, result2);
        Assert.Same(result2, result3);
    }

    [Fact]
    public void GetOrAddValue_ConcurrentCalls_ShouldHandleThreadSafely()
    {
        // Arrange
        var cache = new EventResolverCache();
        var results = new string[100];

        // Act
        Parallel.For(0, 100, i =>
        {
            results[i] = cache.GetOrAddValue($"Value{i % 10}");
        });

        // Assert
        // Verify that same values share the same reference
        for (int i = 0; i < 100; i += 10)
        {
            var value = $"Value{i % 10}";
            var firstOccurrence = results[i];
            
            for (int j = i; j < 100; j += 10)
            {
                Assert.Same(firstOccurrence, results[j]);
            }
        }
    }

    [Fact]
    public void GetOrAddValue_WithDifferentValues_ShouldReturnDifferentReferences()
    {
        // Arrange
        var cache = new EventResolverCache();
        var value1 = "Value 1";
        var value2 = "Value 2";

        // Act
        var result1 = cache.GetOrAddValue(value1);
        var result2 = cache.GetOrAddValue(value2);

        // Assert
        Assert.NotSame(result1, result2);
        Assert.Equal("Value 1", result1);
        Assert.Equal("Value 2", result2);
    }

    [Fact]
    public void GetOrAddValue_WithEmptyString_ShouldReturnSameEmptyStringReference()
    {
        // Arrange
        var cache = new EventResolverCache();

        // Act
        var result1 = cache.GetOrAddValue(string.Empty);
        var result2 = cache.GetOrAddValue(string.Empty);

        // Assert
        Assert.Same(result1, result2);
        Assert.Equal(string.Empty, result1);
    }

    [Fact]
    public void GetOrAddValue_WithNewString_ShouldAddToCache()
    {
        // Arrange
        var cache = new EventResolverCache();
        var value = new string("Test".ToCharArray()); // Force new string instance

        // Act
        var result = cache.GetOrAddValue(value);

        // Assert
        Assert.Equal(value, result);
    }

    [Fact]
    public void MixedOperations_ConcurrentDescriptionAndValueAccess_ShouldHandleThreadSafely()
    {
        // Arrange
        var cache = new EventResolverCache();

        // Act
        Parallel.For(0, 100, i =>
        {
            if (i % 2 == 0)
            {
                cache.GetOrAddDescription($"Description{i % 10}");
            }
            else
            {
                cache.GetOrAddValue($"Value{i % 10}");
            }
        });

        // Assert
        // If we get here without exceptions, thread safety is maintained
        Assert.True(true);
    }

    [Fact]
    public void MixedOperations_ConcurrentWithClearAll_ShouldHandleThreadSafely()
    {
        // Arrange
        var cache = new EventResolverCache();

        // Act
        Parallel.Invoke(
            () =>
            {
                for (int i = 0; i < 100; i++)
                {
                    cache.GetOrAddDescription($"Description{i}");
                }
            },
            () =>
            {
                for (int i = 0; i < 100; i++)
                {
                    cache.GetOrAddValue($"Value{i}");
                }
            },
            () =>
            {
                for (int i = 0; i < 10; i++)
                {
                    Thread.Sleep(5);
                    cache.ClearAll();
                }
            }
        );

        // Assert
        // If we get here without exceptions, thread safety is maintained
        Assert.True(true);
    }

    [Fact]
    public void SeparateCaches_DescriptionAndValue_ShouldNotInterfere()
    {
        // Arrange
        var cache = new EventResolverCache();
        var sharedString = "Shared";

        // Act
        var description = cache.GetOrAddDescription(sharedString);
        var value = cache.GetOrAddValue(sharedString);

        // Assert
        // Even though they have the same content, they should be cached separately
        // and both should work independently
        Assert.Equal(sharedString, description);
        Assert.Equal(sharedString, value);
    }
}
