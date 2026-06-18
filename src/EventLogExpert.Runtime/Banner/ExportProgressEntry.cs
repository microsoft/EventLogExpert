// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Banner;

/// <summary>
///     A single in-flight event export shown as an indeterminate progress banner. <paramref name="Cancel" /> requests
///     cancellation of the underlying streaming write.
/// </summary>
public sealed record ExportProgressEntry(string Message, Action Cancel);
