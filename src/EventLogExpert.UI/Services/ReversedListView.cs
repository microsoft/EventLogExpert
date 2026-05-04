// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Collections;

namespace EventLogExpert.UI.Services;

public sealed class ReversedListView<T> : IReadOnlyList<T>, IList<T>
{
    private readonly IList<T> _inner;

    public ReversedListView(IList<T> inner)
    {
        ArgumentNullException.ThrowIfNull(inner);

        _inner = inner;
    }

    public int Count => _inner.Count;

    public bool IsReadOnly => true;

    public T this[int index]
    {
        get => _inner[_inner.Count - 1 - index];
        set => throw new NotSupportedException();
    }

    public void Add(T item) => throw new NotSupportedException();

    public void Clear() => throw new NotSupportedException();

    public bool Contains(T item) => _inner.Contains(item);

    public void CopyTo(T[] array, int arrayIndex)
    {
        ArgumentNullException.ThrowIfNull(array);
        ArgumentOutOfRangeException.ThrowIfNegative(arrayIndex);

        if (array.Length - arrayIndex < _inner.Count)
        {
            throw new ArgumentException("Destination array is too small.", nameof(array));
        }

        for (var i = 0; i < _inner.Count; i++)
        {
            array[arrayIndex + i] = this[i];
        }
    }

    public IEnumerator<T> GetEnumerator()
    {
        for (var i = 0; i < _inner.Count; i++)
        {
            yield return this[i];
        }
    }

    public int IndexOf(T item)
    {
        for (var i = 0; i < _inner.Count; i++)
        {
            if (EqualityComparer<T>.Default.Equals(this[i], item))
            {
                return i;
            }
        }

        return -1;
    }

    public void Insert(int index, T item) => throw new NotSupportedException();

    public bool Remove(T item) => throw new NotSupportedException();

    public void RemoveAt(int index) => throw new NotSupportedException();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
