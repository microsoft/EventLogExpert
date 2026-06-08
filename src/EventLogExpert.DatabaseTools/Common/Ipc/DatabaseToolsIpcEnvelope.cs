// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Common.Operations;
using Microsoft.Extensions.Logging;
using System.Text.Json.Serialization;

namespace EventLogExpert.DatabaseTools.Common.Ipc;

/// <summary>
///     Polymorphic base for envelopes that flow over the elevation-helper IPC channel. The discriminator <c>$type</c>
///     selects the concrete envelope at deserialization time; callers always serialize through this base so the
///     discriminator is emitted. Derived envelope records live in this same file so the polymorphic schema stays
///     reviewable in one place.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(HelloEnvelope), "hello")]
[JsonDerivedType(typeof(ProbeEnvelope), "probe")]
[JsonDerivedType(typeof(LogEnvelope), "log")]
[JsonDerivedType(typeof(ProgressEnvelope), "progress")]
[JsonDerivedType(typeof(ResultEnvelope), "result")]
[JsonDerivedType(typeof(FatalEnvelope), "fatal")]
[JsonDerivedType(typeof(CancelEnvelope), "cancel")]
public abstract record DatabaseToolsIpcEnvelope;

/// <summary>
///     First envelope sent helper → runner immediately after pipe connect. Carries the helper's process id (used by
///     the runner to verify the connected pipe client matches the spawned helper) plus a single protocol-version int so
///     future schema changes can be detected before any operation runs.
/// </summary>
/// <param name="HelperProcessId">OS process id of the helper, captured at startup.</param>
/// <param name="ProtocolVersion">Schema version of the envelope contract; bump on breaking changes.</param>
public sealed record HelloEnvelope(int HelperProcessId, int ProtocolVersion) : DatabaseToolsIpcEnvelope
{
    public const int CurrentProtocolVersion = 1;
}

/// <summary>
///     Diagnostic envelope written by the helper when started with the <c>--probe</c> CLI flag. Captures environment
///     facts the main app cannot observe from medium-IL: actual process path, integrity level, whether
///     <see cref="Windows.ApplicationModel.Package.Current" /> resolves under elevation, and whether enumerating local
///     event-log providers succeeds. The probe confirms the elevation-helper deployment pipeline works end-to-end.
/// </summary>
/// <param name="ProcessPath">Value of <see cref="System.Environment.ProcessPath" /> at startup.</param>
/// <param name="IntegrityLevel">Process token integrity level: "high", "medium", "low", or "unknown".</param>
/// <param name="PackageIdentityOk">True iff <c>Package.Current</c> resolved without throwing.</param>
/// <param name="PackageIdentityError">Message captured if <c>Package.Current</c> threw; null otherwise.</param>
/// <param name="LocalProviderEnumerationOk">
///     True iff <c>EventLogSession.GlobalSession.GetProviderNames()</c> returned a
///     non-empty list without throwing.
/// </param>
/// <param name="LocalProviderEnumerationError">Message captured if enumeration threw; null otherwise.</param>
/// <param name="LocalProviderCount">Count of providers enumerated (informational; 0 if enumeration failed).</param>
public sealed record ProbeEnvelope(
    string ProcessPath,
    string IntegrityLevel,
    bool PackageIdentityOk,
    string? PackageIdentityError,
    bool LocalProviderEnumerationOk,
    string? LocalProviderEnumerationError,
    int LocalProviderCount) : DatabaseToolsIpcEnvelope;

/// <summary>Streamed log entry from a helper-side operation. Mirrors <see cref="LogRecord" /> contents.</summary>
/// <param name="TimestampUtc">When the entry was emitted.</param>
/// <param name="Level">Severity level.</param>
/// <param name="Message">Materialized message text.</param>
public sealed record LogEnvelope(DateTime TimestampUtc, LogLevel Level, string Message) : DatabaseToolsIpcEnvelope;

/// <summary>Streamed progress report from a helper-side operation. Mirrors <see cref="DatabaseToolsProgress" /> contents.</summary>
/// <param name="Processed">Items completed so far.</param>
/// <param name="Total">Expected total items, or null if unknown.</param>
/// <param name="CurrentItem">Optional label identifying the item being processed.</param>
public sealed record ProgressEnvelope(int Processed, int? Total, string? CurrentItem) : DatabaseToolsIpcEnvelope;

/// <summary>
///     Terminal envelope from a helper-side operation. The presence of this envelope before pipe close signals an
///     orderly completion.
/// </summary>
/// <param name="Outcome">Operation outcome.</param>
/// <param name="FailureSummary">
///     Failure summary set when <see cref="Outcome" /> is
///     <see cref="DatabaseToolsOutcome.Failed" />.
/// </param>
/// <param name="DurationMs">Operation duration in milliseconds.</param>
public sealed record ResultEnvelope(DatabaseToolsOutcome Outcome, string? FailureSummary, long DurationMs) : DatabaseToolsIpcEnvelope;

/// <summary>
///     Emitted by the helper when an unhandled exception escapes the operation dispatcher. Distinguishes "helper
///     crashed unexpectedly" from "operation completed with Failed outcome" — the latter uses
///     <see cref="ResultEnvelope" />.
/// </summary>
/// <param name="ExceptionType">Fully-qualified type name of the unhandled exception.</param>
/// <param name="Message">Exception message.</param>
/// <param name="StackTrace">Stack trace (may be empty when stripped by trimming).</param>
public sealed record FatalEnvelope(string ExceptionType, string Message, string StackTrace) : DatabaseToolsIpcEnvelope;

/// <summary>
///     Control envelope written runner → helper to request cooperative cancellation of the in-flight operation.
///     Helper-side dedicated control reader cancels the operation's linked <c>CancellationTokenSource</c>; the operation
///     then completes via the normal <see cref="ResultEnvelope" /> path with <see cref="DatabaseToolsOutcome.Cancelled" />
///     .
/// </summary>
public sealed record CancelEnvelope : DatabaseToolsIpcEnvelope;
