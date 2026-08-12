// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Text.Json;

namespace EventLogExpert.Runtime.Tests.Architecture;

public sealed class RuntimePackageNeutralityTests
{
    private const string ForbiddenPackageIdPrefix = "Microsoft.AspNetCore.";
    private const string RepositoryMarkerFile = "EventLogExpert.slnx";

    private static readonly string[] s_forbiddenPackageIds = ["Fluxor.Blazor.Web"];

    [Fact]
    public void RuntimeRestoreGraph_ContainsNoUIToolkitPackages()
    {
        IReadOnlyList<string> restoredPackageIds = ReadRestoredPackageIds();

        Assert.NotEmpty(restoredPackageIds);

        string[] forbidden = [.. restoredPackageIds
            .Where(IsForbidden)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)];

        Assert.Empty(forbidden);
    }

    private static bool IsForbidden(string packageId) =>
        packageId.StartsWith(ForbiddenPackageIdPrefix, StringComparison.OrdinalIgnoreCase) ||
        s_forbiddenPackageIds.Contains(packageId, StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyList<string> ReadRestoredPackageIds()
    {
        string assetsPath = LocateRuntimeAssetsFile();

        Assert.True(File.Exists(assetsPath), $"Expected a restore graph at '{assetsPath}'. Run a restore first.");

        using FileStream stream = File.OpenRead(assetsPath);
        using JsonDocument document = JsonDocument.Parse(stream);

        List<string> packageIds = [];

        foreach (JsonProperty target in document.RootElement.GetProperty("targets").EnumerateObject())
        {
            foreach (JsonProperty library in target.Value.EnumerateObject())
            {
                int separatorIndex = library.Name.IndexOf('/', StringComparison.Ordinal);

                packageIds.Add(separatorIndex < 0 ? library.Name : library.Name[..separatorIndex]);
            }
        }

        return packageIds;
    }

    private static string LocateRuntimeAssetsFile()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, RepositoryMarkerFile)))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);

        return Path.Combine(directory.FullName, "src", "EventLogExpert.Runtime", "obj", "project.assets.json");
    }
}
