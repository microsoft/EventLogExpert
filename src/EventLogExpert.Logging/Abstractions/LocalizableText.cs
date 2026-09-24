// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Logging.Abstractions;

public sealed record LocalizableText(string Key, IReadOnlyList<string> Args);
