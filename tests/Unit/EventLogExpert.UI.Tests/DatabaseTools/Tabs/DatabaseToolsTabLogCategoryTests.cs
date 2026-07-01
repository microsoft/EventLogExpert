// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.UI.DatabaseTools.Tabs;
using System.Reflection;

namespace EventLogExpert.UI.Tests.DatabaseTools.Tabs;

public sealed class DatabaseToolsTabLogCategoryTests
{
    [Theory]
    [InlineData(typeof(CreateDatabaseTab), LogCategories.DatabaseToolsCreate)]
    [InlineData(typeof(MergeDatabaseTab), LogCategories.DatabaseToolsMerge)]
    [InlineData(typeof(DiffDatabasesTab), LogCategories.DatabaseToolsDiff)]
    [InlineData(typeof(UpgradeDatabaseTab), LogCategories.DatabaseToolsUpgrade)]
    [InlineData(typeof(ShowProvidersTab), LogCategories.DatabaseToolsShow)]
    public void LogCategory_ShouldMapEachTabToItsFineCategory(Type tabType, string expectedCategory)
    {
        var tab = Activator.CreateInstance(tabType)!;
        var property = tabType.GetProperty("LogCategory", BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.NotNull(property);
        Assert.Equal(expectedCategory, property.GetValue(tab));
    }
}
