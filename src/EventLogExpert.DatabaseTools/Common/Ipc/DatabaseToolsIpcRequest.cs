// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.CreateDatabase;
using EventLogExpert.DatabaseTools.DiffDatabase;
using EventLogExpert.DatabaseTools.MergeDatabase;
using EventLogExpert.DatabaseTools.ShowProviders;
using EventLogExpert.DatabaseTools.UpgradeDatabase;
using System.Text.Json.Serialization;

namespace EventLogExpert.DatabaseTools.Common.Ipc;

/// <summary>
///     Polymorphic base for the single-shot request payload sent runner → helper at operation start. Each derived
///     type wraps its existing public <c>*Request</c> record so the operation contract stays canonical (no parallel
///     IPC-only request shape). All five existing operations are supported so a single helper binary can dispatch any
///     operation — the UI side decides which derived type to send based on which tab invoked the elevation.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(ShowProvidersIpcRequest), "show")]
[JsonDerivedType(typeof(CreateDatabaseIpcRequest), "create")]
[JsonDerivedType(typeof(MergeDatabaseIpcRequest), "merge")]
[JsonDerivedType(typeof(DiffDatabaseIpcRequest), "diff")]
[JsonDerivedType(typeof(UpgradeDatabaseIpcRequest), "upgrade")]
[JsonDerivedType(typeof(ListImageEditionsIpcRequest), "list-editions")]
public abstract record DatabaseToolsIpcRequest(bool Verbose);

/// <param name="Request">The matching domain request record.</param>
/// <param name="Verbose">Lowers the helper's streaming-trace-logger threshold to Trace when true.</param>
public sealed record ShowProvidersIpcRequest(ShowProvidersRequest Request, bool Verbose) : DatabaseToolsIpcRequest(Verbose);

/// <param name="Request">The matching domain request record.</param>
/// <param name="Verbose">Lowers the helper's streaming-trace-logger threshold to Trace when true.</param>
public sealed record CreateDatabaseIpcRequest(CreateDatabaseRequest Request, bool Verbose) : DatabaseToolsIpcRequest(Verbose);

/// <param name="Request">The matching domain request record.</param>
/// <param name="Verbose">Lowers the helper's streaming-trace-logger threshold to Trace when true.</param>
public sealed record MergeDatabaseIpcRequest(MergeDatabaseRequest Request, bool Verbose) : DatabaseToolsIpcRequest(Verbose);

/// <param name="Request">The matching domain request record.</param>
/// <param name="Verbose">Lowers the helper's streaming-trace-logger threshold to Trace when true.</param>
public sealed record DiffDatabaseIpcRequest(DiffDatabaseRequest Request, bool Verbose) : DatabaseToolsIpcRequest(Verbose);

/// <param name="Request">The matching domain request record.</param>
/// <param name="Verbose">Lowers the helper's streaming-trace-logger threshold to Trace when true.</param>
public sealed record UpgradeDatabaseIpcRequest(UpgradeDatabaseRequest Request, bool Verbose) : DatabaseToolsIpcRequest(Verbose);

/// <summary>
///     Read-only request to enumerate an offline image's editions (the <c>--wim-index</c> choices). Unlike the five
///     operation requests it returns its payload through a streamed <see cref="ImageEditionsMessage" /> before the
///     terminal result, rather than persisting anything.
/// </summary>
/// <param name="Request">The matching domain request record.</param>
/// <param name="Verbose">Lowers the helper's streaming-trace-logger threshold to Trace when true.</param>
public sealed record ListImageEditionsIpcRequest(ListOfflineImageEditionsRequest Request, bool Verbose) : DatabaseToolsIpcRequest(Verbose);
