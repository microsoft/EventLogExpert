// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Contracts;
using EventLogExpert.DatabaseTools.Operations;
using EventLogExpert.Runtime.DatabaseTools;

namespace EventLogExpert.Runtime.Tests.DatabaseTools;

/// <summary>
///     Verifies that <see cref="DatabaseToolsOperationFactory" />'s overloads dispatch to the correct concrete
///     <c>*Operation</c> type. Catches copy-paste typos (e.g.,
///     <c>Create(ShowProvidersRequest) =&gt; new MergeDatabaseOperation(...)</c>) that the service-level tests with fake
///     factories would not detect.
/// </summary>
public sealed class DatabaseToolsOperationFactoryTests
{
    [Fact]
    public void Create_CreateDatabaseRequest_ReturnsCreateDatabaseOperation()
        => Assert.IsType<CreateDatabaseOperation>(
            new DatabaseToolsOperationFactory().Create(new CreateDatabaseRequest("target.db", null, null, null)));

    [Fact]
    public void Create_DiffDatabaseRequest_ReturnsDiffDatabaseOperation()
        => Assert.IsType<DiffDatabaseOperation>(
            new DatabaseToolsOperationFactory().Create(new DiffDatabaseRequest("first.db", "second.db", "out.db")));

    [Fact]
    public void Create_MergeDatabaseRequest_ReturnsMergeDatabaseOperation()
        => Assert.IsType<MergeDatabaseOperation>(
            new DatabaseToolsOperationFactory().Create(new MergeDatabaseRequest("source.db", "target.db", false)));

    [Fact]
    public void Create_ShowProvidersRequest_ReturnsShowProvidersOperation()
        => Assert.IsType<ShowProvidersOperation>(
            new DatabaseToolsOperationFactory().Create(new ShowProvidersRequest(null, null)));

    [Fact]
    public void Create_UpgradeDatabaseRequest_ReturnsUpgradeDatabaseOperation()
        => Assert.IsType<UpgradeDatabaseOperation>(
            new DatabaseToolsOperationFactory().Create(new UpgradeDatabaseRequest("target.db")));
}
