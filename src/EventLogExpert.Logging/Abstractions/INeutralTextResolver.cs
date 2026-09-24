// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Logging.Abstractions;

public interface INeutralTextResolver
{
    string Resolve(string key, IReadOnlyList<string> args);
}
