// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Tests.DatabaseTools.Elevation.TestUtils;

internal sealed class ListProgress<T> : IProgress<T>
{
    private readonly List<T> _entries = [];
    private readonly Lock _lock = new();

    public IReadOnlyList<T> Entries
    {
        get
        {
            lock (_lock) { return [.. _entries]; }
        }
    }

    public void Report(T value)
    {
        lock (_lock) { _entries.Add(value); }
    }
}

internal sealed class ThrowingProgress<T>(string failureMessage = "boom") : IProgress<T>
{
    public void Report(T value) => throw new InvalidOperationException(failureMessage);
}
