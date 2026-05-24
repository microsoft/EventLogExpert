// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Contracts;
using EventLogExpert.DatabaseTools.Operations;

namespace EventLogExpert.Runtime.DatabaseTools;

/// <summary>
///     Constructs <see cref="IDatabaseToolsOperation" /> instances from request records. Pulled out of
///     <see cref="DatabaseToolsService" /> as a test seam: production wires <see cref="DatabaseToolsOperationFactory" />
///     which returns the concrete <c>*Operation</c> types; tests inject a fake factory whose <c>Create</c> overloads
///     return stub <see cref="IDatabaseToolsOperation" /> instances so the real <see cref="DatabaseToolsService" />
///     dispatch + <c>Task.Run</c> wrapper + exception → outcome translation + duration measurement runs against the
///     production code rather than a test-side copy.
/// </summary>
internal interface IDatabaseToolsOperationFactory
{
    IDatabaseToolsOperation Create(ShowProvidersRequest request);

    IDatabaseToolsOperation Create(CreateDatabaseRequest request);

    IDatabaseToolsOperation Create(MergeDatabaseRequest request);

    IDatabaseToolsOperation Create(DiffDatabaseRequest request);

    IDatabaseToolsOperation Create(UpgradeDatabaseRequest request);
}
