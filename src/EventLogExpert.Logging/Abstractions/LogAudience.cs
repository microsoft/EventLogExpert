// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Logging.Abstractions;

/// <summary>
///     Identifies who a <see cref="LogRecord" /> is written for so sinks can route it correctly. Records reach the
///     user-facing operation log only when their audience is <see cref="User" />; developer diagnostics are marked
///     <see cref="Diagnostic" /> and stay in the debug/file log.
/// </summary>
/// <remarks>
///     Localized <c>User</c> messages and raw <c>Data</c> output share the <see cref="User" /> audience because both
///     are meant for the operator. The <c>Trace</c> channel (English interpolated diagnostics from shared reader/imaging
///     components) is <see cref="Diagnostic" />, which keeps unlocalized text and raw exception messages out of the
///     localized operation log while still recording them for troubleshooting.
/// </remarks>
public enum LogAudience
{
    /// <summary>Operator-facing output (localized <c>User</c> messages and raw <c>Data</c> lines); shown in the operation log.</summary>
    User = 0,

    /// <summary>
    ///     Developer diagnostics (the <c>Trace</c> channel); recorded in the debug/file log but hidden from the operation
    ///     log.
    /// </summary>
    Diagnostic = 1
}
