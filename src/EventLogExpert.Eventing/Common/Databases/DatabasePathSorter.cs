// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Eventing.Common.Databases;

/// <summary>
///     Sorts provider-database identifiers (full paths or bare file names) into resolver load-priority order, so that
///     load-priority cascading works the same regardless of which layer is invoking it.
/// </summary>
/// <remarks>
///     <para>
///         Order is: ascending by product name, then descending by version (numeric where the version token parses as an
///         integer so "10" sorts after "2"), then descending by raw version string for tie-breaks. Inputs whose
///         extensionless file name does not end in a space-delimited version token are sorted by their full extensionless
///         name with no version key.
///     </para>
///     <para>
///         Returned strings are the originals — directory and extension are used only for key extraction, never for
///         reconstruction. A caller passing full paths gets full paths back; a caller passing bare file names gets bare
///         file names back.
///     </para>
/// </remarks>
public static class DatabasePathSorter
{
    /// <summary>Sort identifiers in resolver load-priority order. Returns originals unchanged.</summary>
    public static IReadOnlyList<string> Sort(IEnumerable<string> identifiers)
    {
        ArgumentNullException.ThrowIfNull(identifiers);

        var source = identifiers as IReadOnlyList<string> ?? [.. identifiers];

        if (source.Count == 0) { return []; }

        if (source.Count == 1) { return source as string[] ?? [.. source]; }

        var keys = new SortKey[source.Count];

        for (int i = 0; i < source.Count; i++)
        {
            keys[i] = SortKey.From(source[i]);
        }

        Array.Sort(keys);

        var sorted = new string[source.Count];

        for (int i = 0; i < source.Count; i++)
        {
            sorted[i] = keys[i].Original;
        }

        return sorted;
    }

    private readonly record struct SortKey(string Original, string ProductPart, int? NumericVersion, string VersionPart)
        : IComparable<SortKey>
    {
        public static SortKey From(string identifier)
        {
            var nameSpan = Path.GetFileNameWithoutExtension(identifier.AsSpan());
            int lastSpace = nameSpan.LastIndexOf(' ');

            if (lastSpace <= 0 || lastSpace >= nameSpan.Length - 1)
            {
                return new SortKey(identifier, nameSpan.ToString(), null, string.Empty);
            }

            var versionSpan = nameSpan[(lastSpace + 1)..];
            var productSpan = nameSpan[..lastSpace];
            int? numericVersion = int.TryParse(versionSpan, out int parsed) ? parsed : null;

            return new SortKey(identifier, productSpan.ToString(), numericVersion, versionSpan.ToString());
        }

        public int CompareTo(SortKey other)
        {
            int productOrder = string.CompareOrdinal(ProductPart, other.ProductPart);

            if (productOrder != 0) { return productOrder; }

            int thisNumeric = NumericVersion ?? int.MinValue;
            int otherNumeric = other.NumericVersion ?? int.MinValue;

            return thisNumeric != otherNumeric ? otherNumeric.CompareTo(thisNumeric) : string.CompareOrdinal(other.VersionPart, VersionPart);
        }
    }
}
