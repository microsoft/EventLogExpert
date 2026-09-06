// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Banner;

public sealed record FilterLibraryNotFullyLoaded(int Count) : BannerMessage;

public sealed record FilterSetSaveFailed(string Name) : BannerMessage;
