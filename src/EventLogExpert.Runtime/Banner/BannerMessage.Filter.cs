// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Banner;

public sealed record FilterLibraryNotFullyLoaded(int Count) : BannerMessage;

public sealed record FilterLibraryImportFailed : BannerMessage;

public sealed record FilterAddToSetFailed : BannerMessage;

public sealed record FilterSetCreateFailed(string Name) : BannerMessage;

public sealed record FilterSetSaveFailed(string Name) : BannerMessage;

public sealed record FilterSetUpdateFailed : BannerMessage;

public sealed record LibraryEntryDeleteFailed : BannerMessage;

public sealed record LibraryEntryFavoriteFailed : BannerMessage;

public sealed record LibraryEntryPromoteFailed : BannerMessage;

public sealed record LibraryEntryRenameFailed : BannerMessage;

public sealed record LibraryEntrySaveFailed(string Name) : BannerMessage;

public sealed record LibraryEntryTagsSaveFailed : BannerMessage;

public sealed record LibraryEntryUpdateFailed(string Name) : BannerMessage;

public sealed record LibraryTagsBulkUpdateFailed : BannerMessage;
