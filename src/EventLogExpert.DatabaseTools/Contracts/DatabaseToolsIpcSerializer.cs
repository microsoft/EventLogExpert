// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Text.Json;

namespace EventLogExpert.DatabaseTools.Contracts;

/// <summary>
///     Factory for the single <see cref="JsonSerializerOptions" /> instance every IPC participant uses. The options
///     bind <see cref="DatabaseToolsIpcJsonContext" /> as the source-gen resolver AND register
///     <see cref="RegexJsonConverter" /> as a runtime converter — required because the Metadata source-gen mode does not
///     bake converters into the fast path. Callers MUST share a single instance per process (System.Text.Json caches
///     resolver state on first use of an options instance).
/// </summary>
public static class DatabaseToolsIpcSerializer
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            TypeInfoResolver = DatabaseToolsIpcJsonContext.Default,
            WriteIndented = false
        };

        options.Converters.Add(new RegexJsonConverter());

        return options;
    }
}
