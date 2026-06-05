// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.FilterEditor;
using EventLogExpert.UI.FilterLibrary;
using Microsoft.AspNetCore.Components;
using System.Reflection;

namespace EventLogExpert.UI.Tests.FilterEditor;

public sealed class WrapperConformanceTests
{
    private static readonly HashSet<string> s_expectedFilterRowParameterNames = new(StringComparer.Ordinal)
    {
        "Id",
        "Value",
        "PendingDraft",
        "OnPendingDiscard",
        "OnPendingSave",
        "OnRemoved",
        "OnEditingChanged",
    };

    private static readonly HashSet<string> s_expectedLibraryFilterRowParameterNames = new(StringComparer.Ordinal)
    {
        "Id",
        "Value",
        "PendingDraft",
        "OnSave",
        "OnRemove",
        "OnExclusionChanged",
        "OnToggleEnabled",
        "OnEdit",
        "OnCancel",
        "OnPendingSave",
        "OnPendingDiscard",
    };

    [Fact]
    public void FilterRow_ExternalParameterSurface_MatchesExpectedSnapshot()
    {
        var actual = GetParameterNames(typeof(FilterRow));

        var missing = s_expectedFilterRowParameterNames.Except(actual, StringComparer.Ordinal).ToList();
        var extra = actual.Except(s_expectedFilterRowParameterNames, StringComparer.Ordinal).ToList();

        Assert.Empty(missing);
        Assert.Empty(extra);
    }

    [Fact]
    public void LibraryFilterRow_ExternalParameterSurface_MatchesExpectedSnapshot()
    {
        var actual = GetParameterNames(typeof(LibraryFilterRow));

        var missing = s_expectedLibraryFilterRowParameterNames.Except(actual, StringComparer.Ordinal).ToList();
        var extra = actual.Except(s_expectedLibraryFilterRowParameterNames, StringComparer.Ordinal).ToList();

        Assert.Empty(missing);
        Assert.Empty(extra);
    }

    private static HashSet<string> GetParameterNames(Type t) =>
        new(
            t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(p => p.GetCustomAttribute<ParameterAttribute>() is not null)
                .Select(p => p.Name),
            StringComparer.Ordinal);
}
