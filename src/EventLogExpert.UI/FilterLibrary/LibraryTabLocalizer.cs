// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Localization;
using Microsoft.Extensions.Localization;

namespace EventLogExpert.UI.FilterLibrary;

internal static class LibraryTabLocalizer
{
    internal static string Label(IStringLocalizer<SharedResource> localizer, LibraryTab tab) => tab switch
    {
        LibraryTab.Saved => localizer["LibraryTab_Saved"],
        LibraryTab.Favorites => localizer["LibraryTab_Favorites"],
        LibraryTab.PreviouslyUsed => localizer["LibraryTab_PreviouslyUsed"],
        _ => throw new ArgumentOutOfRangeException(nameof(tab), tab, null)
    };
}
