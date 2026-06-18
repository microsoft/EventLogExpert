// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Collections.Immutable;

namespace EventLogExpert.Runtime.Common.Files;

public static class FileSaveFileTypes
{
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Csv =
        ImmutableDictionary<string, IReadOnlyList<string>>.Empty
            .Add("CSV", [".csv"]);

    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Json =
        ImmutableDictionary<string, IReadOnlyList<string>>.Empty
            .Add("JSON", [".json"]);

    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Log =
        ImmutableDictionary<string, IReadOnlyList<string>>.Empty
            .Add("Log files", [".log", ".txt"]);
}
