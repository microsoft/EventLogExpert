// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Resolvers;

namespace EventLogExpert.Eventing.Tests.Resolvers;

public sealed class EventResolverCacheTests
{
    public enum CacheKind { Description, Value }

    [Fact]
    public void ClearAll_AfterAddingItems_ShouldClearBothCaches()
    {
        // Arrange
        var cache = new EventResolverCache();

        // Force new string instances to avoid string interning
        var testDesc = new string("Test Description".ToCharArray());
        var testValue = new string("Test Value".ToCharArray());

        var oldDesc = cache.GetOrAddDescription(testDesc);
        var oldValue = cache.GetOrAddValue(testValue);

        // Act
        cache.ClearAll();

        // Assert
        // After clearing, calling GetOrAdd with new instances should return those new instances
        var newDescInput = new string("Test Description".ToCharArray());
        var newValueInput = new string("Test Value".ToCharArray());

        var newDesc = cache.GetOrAddDescription(newDescInput);
        var newValue = cache.GetOrAddValue(newValueInput);

        Assert.NotSame(oldDesc, newDesc); // Different reference after clear (description cache)
        Assert.NotSame(oldValue, newValue); // Different reference after clear (value cache)

        // But subsequent calls with the same inputs should return cached instances
        var desc2 = cache.GetOrAddDescription(new string("Test Description".ToCharArray()));
        var value2 = cache.GetOrAddValue(new string("Test Value".ToCharArray()));

        Assert.Same(newDesc, desc2); // Should cache the new description instance
        Assert.Same(newValue, value2); // Should cache the new value instance
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
        var exception = Record.Exception(() => Parallel.For(0, 10, _ => cache.ClearAll()));

        // Assert
        Assert.Null(exception);

        // Verify post-condition: after clear, new inputs return new instances
        var newInput = new string("PostClear".ToCharArray());
        var result = cache.GetOrAddDescription(newInput);
        Assert.Same(newInput, result);
    }

    [Fact]
    public void ClearAll_WithEmptyCaches_ShouldNotThrow()
    {
        // Arrange
        var cache = new EventResolverCache();

        // Act
        var exception = Record.Exception(() => cache.ClearAll());

        // Assert
        Assert.Null(exception);
    }

    [Theory]
    [InlineData(CacheKind.Description)]
    [InlineData(CacheKind.Value)]
    public void GetOrAdd_CalledMultipleTimes_ShouldReturnSameReference(CacheKind kind)
    {
        // Arrange
        var cache = new EventResolverCache();
        var input = $"Test {kind}";

        // Act
        var result1 = GetOrAdd(cache, kind, input);
        var result2 = GetOrAdd(cache, kind, input);
        var result3 = GetOrAdd(cache, kind, input);

        // Assert
        Assert.Same(result1, result2);
        Assert.Same(result2, result3);
    }

    [Theory]
    [InlineData(CacheKind.Description)]
    [InlineData(CacheKind.Value)]
    public void GetOrAdd_ConcurrentCalls_ShouldHandleThreadSafely(CacheKind kind)
    {
        // Arrange
        var cache = new EventResolverCache();
        var results = new string[100];

        // Act
        Parallel.For(0, 100, i =>
        {
            results[i] = GetOrAdd(cache, kind, $"{kind}{i % 10}");
        });

        // Assert
        // 100 calls were spread across 10 distinct keys ("{kind}0" .. "{kind}9"), 10 calls per key.
        // For each key, every occurrence must be the same reference (de-dup contract under
        // contention). The previous loop only stepped i by 10, which meant i % 10 was always 0
        // and only key "{kind}0" was actually validated; iterating each key 0..9 explicitly
        // covers all ten distinct cache slots so a regression in any one of them fails the test.
        for (int key = 0; key < 10; key++)
        {
            var expected = results[key];
            Assert.NotNull(expected);

            for (int occurrence = 1; occurrence < 10; occurrence++)
            {
                Assert.Same(expected, results[key + (occurrence * 10)]);
            }
        }
    }

    [Theory]
    [InlineData(CacheKind.Description, "Description 1", "Description 2")]
    [InlineData(CacheKind.Value, "Value 1", "Value 2")]
    public void GetOrAdd_WithDifferentInputs_ShouldReturnDifferentReferences(CacheKind kind, string a, string b)
    {
        // Arrange
        var cache = new EventResolverCache();

        // Act
        var result1 = GetOrAdd(cache, kind, a);
        var result2 = GetOrAdd(cache, kind, b);

        // Assert
        Assert.NotSame(result1, result2);
        Assert.Equal(a, result1);
        Assert.Equal(b, result2);
    }

    [Theory]
    [InlineData(CacheKind.Description)]
    [InlineData(CacheKind.Value)]
    public void GetOrAdd_WithEmptyString_ShouldReturnSameEmptyStringReference(CacheKind kind)
    {
        // Arrange
        var cache = new EventResolverCache();

        // Act
        var result1 = GetOrAdd(cache, kind, string.Empty);
        var result2 = GetOrAdd(cache, kind, string.Empty);

        // Assert
        Assert.Same(result1, result2);
        Assert.Equal(string.Empty, result1);
    }

    [Theory]
    [InlineData(CacheKind.Description)]
    [InlineData(CacheKind.Value)]
    public void GetOrAdd_WithNewString_ShouldAddToCache(CacheKind kind)
    {
        // Arrange
        var cache = new EventResolverCache();
        var input = new string("Test".ToCharArray()); // Force new string instance

        // Act
        var result = GetOrAdd(cache, kind, input);

        // Assert
        Assert.Equal(input, result);
    }

    [Fact]
    public void MixedOperations_ConcurrentDescriptionAndValueAccess_ShouldHandleThreadSafely()
    {
        // Arrange
        var cache = new EventResolverCache();

        // Act
        var exception = Record.Exception(() =>
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
            }));

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public void MixedOperations_ConcurrentWithClearAll_ShouldHandleThreadSafely()
    {
        // Arrange
        var cache = new EventResolverCache();

        // Act
        var exception = Record.Exception(() =>
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
            ));

        // Assert
        Assert.Null(exception);
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

    private static string GetOrAdd(EventResolverCache cache, CacheKind kind, string input) =>
        kind == CacheKind.Description ? cache.GetOrAddDescription(input) : cache.GetOrAddValue(input);
}
