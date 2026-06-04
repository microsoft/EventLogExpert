// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.CreateDatabase;
using EventLogExpert.DatabaseTools.DiffDatabase;
using EventLogExpert.DatabaseTools.MergeDatabase;
using EventLogExpert.DatabaseTools.ShowProviders;
using EventLogExpert.DatabaseTools.UpgradeDatabase;

namespace EventLogExpert.DatabaseTools.Common.Operations;

/// <summary>
///     Production <see cref="IDatabaseToolsOperationFactory" /> implementation. Each overload returns a fresh
///     concrete <c>*Operation</c> bound to the request record. Stateless and singleton-safe.
/// </summary>
internal sealed class DatabaseToolsOperationFactory : IDatabaseToolsOperationFactory
{
    public IDatabaseToolsOperation Create(ShowProvidersRequest request) => new ShowProvidersOperation(request);

    public IDatabaseToolsOperation Create(CreateDatabaseRequest request) => new CreateDatabaseOperation(request);

    public IDatabaseToolsOperation Create(MergeDatabaseRequest request) => new MergeDatabaseOperation(request);

    public IDatabaseToolsOperation Create(DiffDatabaseRequest request) => new DiffDatabaseOperation(request);

    public IDatabaseToolsOperation Create(UpgradeDatabaseRequest request) => new UpgradeDatabaseOperation(request);
}
