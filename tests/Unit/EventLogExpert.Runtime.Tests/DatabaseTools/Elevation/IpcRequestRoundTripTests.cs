// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Contracts;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EventLogExpert.Runtime.Tests.DatabaseTools.Elevation;

public sealed class IpcRequestRoundTripTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CreateDatabaseIpcRequest_RoundTrips_PreservesRegexNullable(bool verbose)
    {
        var domain = new CreateDatabaseRequest(
            TargetPath: @"C:\out\target.db",
            SourcePath: null,
            FilterRegex: null,
            SkipProvidersInFile: null);
        var original = new CreateDatabaseIpcRequest(domain, verbose);

        var roundTripped = SerializeDeserialize(original, out var json);

        AssertDiscriminator(json, "create");
        var create = Assert.IsType<CreateDatabaseIpcRequest>(roundTripped);
        Assert.Equal(domain.TargetPath, create.Request.TargetPath);
        Assert.Null(create.Request.SourcePath);
        Assert.Null(create.Request.FilterRegex);
        Assert.Null(create.Request.SkipProvidersInFile);
        Assert.Equal(verbose, create.Verbose);
    }

    [Fact]
    public void CreateDatabaseIpcRequest_WithFilterRegex_RoundTripsRegexThroughConverter()
    {
        var domain = new CreateDatabaseRequest(
            TargetPath: @"C:\out\target.db",
            SourcePath: @"C:\src\file.evtx",
            FilterRegex: new Regex("^Microsoft-", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(750)),
            SkipProvidersInFile: @"C:\skip.db");
        var original = new CreateDatabaseIpcRequest(domain, Verbose: false);

        var roundTripped = SerializeDeserialize(original, out _);

        var create = Assert.IsType<CreateDatabaseIpcRequest>(roundTripped);
        Assert.NotNull(create.Request.FilterRegex);
        Assert.Equal("^Microsoft-", create.Request.FilterRegex.ToString());
        Assert.Equal(RegexOptions.IgnoreCase, create.Request.FilterRegex.Options);
        Assert.Equal(TimeSpan.FromMilliseconds(750), create.Request.FilterRegex.MatchTimeout);
    }

    [Fact]
    public void DiffDatabaseIpcRequest_RoundTrips_PreservesThreePaths()
    {
        var domain = new DiffDatabaseRequest(
            FirstSourcePath: @"C:\first.db",
            SecondSourcePath: @"C:\second.db",
            NewDatabasePath: @"C:\diff.db");
        var original = new DiffDatabaseIpcRequest(domain, Verbose: true);

        var roundTripped = SerializeDeserialize(original, out var json);

        AssertDiscriminator(json, "diff");
        var diff = Assert.IsType<DiffDatabaseIpcRequest>(roundTripped);
        Assert.Equal(domain, diff.Request);
        Assert.True(diff.Verbose);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MergeDatabaseIpcRequest_RoundTrips_PreservesOverwriteFlag(bool overwrite)
    {
        var domain = new MergeDatabaseRequest(
            SourcePath: @"C:\src.db",
            TargetDatabasePath: @"C:\target.db",
            Overwrite: overwrite);
        var original = new MergeDatabaseIpcRequest(domain, Verbose: false);

        var roundTripped = SerializeDeserialize(original, out var json);

        AssertDiscriminator(json, "merge");
        var merge = Assert.IsType<MergeDatabaseIpcRequest>(roundTripped);
        Assert.Equal(overwrite, merge.Request.Overwrite);
    }

    [Fact]
    public void ShowProvidersIpcRequest_RoundTrips_WithNullSourceAndRegex()
    {
        var domain = new ShowProvidersRequest(SourcePath: null, FilterRegex: null);
        var original = new ShowProvidersIpcRequest(domain, Verbose: false);

        var roundTripped = SerializeDeserialize(original, out var json);

        AssertDiscriminator(json, "show");
        var show = Assert.IsType<ShowProvidersIpcRequest>(roundTripped);
        Assert.Null(show.Request.SourcePath);
        Assert.Null(show.Request.FilterRegex);
    }

    [Fact]
    public void ShowProvidersIpcRequest_RoundTrips_WithPopulatedRegex()
    {
        var regex = new Regex(@"\bKernel\b", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        var domain = new ShowProvidersRequest(@"C:\src.db", regex);
        var original = new ShowProvidersIpcRequest(domain, Verbose: false);

        var roundTripped = SerializeDeserialize(original, out _);

        var show = Assert.IsType<ShowProvidersIpcRequest>(roundTripped);
        Assert.NotNull(show.Request.FilterRegex);
        Assert.Equal(regex.ToString(), show.Request.FilterRegex.ToString());
        Assert.Equal(regex.Options, show.Request.FilterRegex.Options);
    }

    [Fact]
    public void UpgradeDatabaseIpcRequest_RoundTrips_PreservesDatabasePath()
    {
        var domain = new UpgradeDatabaseRequest(@"C:\db\providers.db");
        var original = new UpgradeDatabaseIpcRequest(domain, Verbose: false);

        var roundTripped = SerializeDeserialize(original, out var json);

        AssertDiscriminator(json, "upgrade");
        var upgrade = Assert.IsType<UpgradeDatabaseIpcRequest>(roundTripped);
        Assert.Equal(@"C:\db\providers.db", upgrade.Request.DatabasePath);
    }

    private static void AssertDiscriminator(string json, string expected)
    {
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("$type", out var typeProperty),
            $"Request JSON missing '$type' discriminator. JSON was: {json}");
        Assert.Equal(expected, typeProperty.GetString());
    }

    private static DatabaseToolsIpcRequest SerializeDeserialize(DatabaseToolsIpcRequest request, out string json)
    {
        json = JsonSerializer.Serialize(request, DatabaseToolsIpcSerializer.Options);

        var roundTripped = JsonSerializer.Deserialize<DatabaseToolsIpcRequest>(
            json, DatabaseToolsIpcSerializer.Options);

        Assert.NotNull(roundTripped);
        return roundTripped;
    }
}
