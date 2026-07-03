// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using System.Reflection;

namespace EventLogExpert.Logging.Tests.Abstractions;

public sealed class LogCategoriesTests
{
    [Fact]
    public void EveryCategoryRootIsInKnownRoots_SoTheDebugLogParserRoundTripsIt()
    {
        var knownRoots = LogCategories.KnownRoots.ToHashSet(StringComparer.Ordinal);

        IEnumerable<FieldInfo> categoryConstants = typeof(LogCategories)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string));

        foreach (FieldInfo constant in categoryConstants)
        {
            var category = (string)constant.GetRawConstantValue()!;
            string root = category.Split('.', 2)[0];

            Assert.True(
                knownRoots.Contains(root),
                $"LogCategories.{constant.Name} = '{category}' has root '{root}' missing from KnownRoots; " +
                "the debug-log parser regex will not round-trip it. Add the root to LogCategories.KnownRoots.");
        }
    }
}
