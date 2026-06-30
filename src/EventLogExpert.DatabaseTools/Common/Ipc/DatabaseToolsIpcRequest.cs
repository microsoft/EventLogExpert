// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.CreateDatabase;
using EventLogExpert.DatabaseTools.DiffDatabase;
using EventLogExpert.DatabaseTools.MergeDatabase;
using EventLogExpert.DatabaseTools.ShowProviders;
using EventLogExpert.DatabaseTools.UpgradeDatabase;
using System.Text.Json.Serialization;

namespace EventLogExpert.DatabaseTools.Common.Ipc;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(ShowProvidersIpcRequest), "show")]
[JsonDerivedType(typeof(CreateDatabaseIpcRequest), "create")]
[JsonDerivedType(typeof(MergeDatabaseIpcRequest), "merge")]
[JsonDerivedType(typeof(DiffDatabaseIpcRequest), "diff")]
[JsonDerivedType(typeof(UpgradeDatabaseIpcRequest), "upgrade")]
[JsonDerivedType(typeof(ListImageEditionsIpcRequest), "list-editions")]
public abstract record DatabaseToolsIpcRequest(bool Verbose);

public sealed record ShowProvidersIpcRequest(ShowProvidersRequest Request, bool Verbose) : DatabaseToolsIpcRequest(Verbose);

public sealed record CreateDatabaseIpcRequest(CreateDatabaseRequest Request, bool Verbose) : DatabaseToolsIpcRequest(Verbose);

public sealed record MergeDatabaseIpcRequest(MergeDatabaseRequest Request, bool Verbose) : DatabaseToolsIpcRequest(Verbose);

public sealed record DiffDatabaseIpcRequest(DiffDatabaseRequest Request, bool Verbose) : DatabaseToolsIpcRequest(Verbose);

public sealed record UpgradeDatabaseIpcRequest(UpgradeDatabaseRequest Request, bool Verbose) : DatabaseToolsIpcRequest(Verbose);

public sealed record ListImageEditionsIpcRequest(ListOfflineImageEditionsRequest Request, bool Verbose) : DatabaseToolsIpcRequest(Verbose);
