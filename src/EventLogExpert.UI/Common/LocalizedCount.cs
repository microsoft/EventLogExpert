// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Localization;
using EventLogExpert.UI.LogTable;
using Microsoft.Extensions.Localization;

namespace EventLogExpert.UI.Common;

internal static class LocalizedCount
{
    internal static string OneOrMany(IStringLocalizer<SharedResource> localizer, int count, string oneKey, string manyKey) =>
        localizer[count == 1 ? oneKey : manyKey, TallyFormatter.Count(count)];

    internal static string OneOrManyRaw(IStringLocalizer<SharedResource> localizer, int count, string oneKey, string manyKey) =>
        localizer[count == 1 ? oneKey : manyKey, count];

    internal static string OneOrManyRaw(
        IStringLocalizer<SharedResource> localizer,
        int count,
        string oneKey,
        string manyKey,
        params object[] extraArgs) =>
        localizer[count == 1 ? oneKey : manyKey, extraArgs];

    internal static string OneOrManyRaw(
        IStringLocalizer<SharedResource> localizer,
        int firstCount,
        int secondCount,
        string oneOneKey,
        string oneManyKey,
        string manyOneKey,
        string manyManyKey,
        params object[] extraArgs) =>
        localizer[(firstCount == 1, secondCount == 1) switch
        {
            (true, true) => oneOneKey,
            (true, false) => oneManyKey,
            (false, true) => manyOneKey,
            _ => manyManyKey,
        }, extraArgs];
}
