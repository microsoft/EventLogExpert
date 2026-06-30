// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.DatabaseTools.CreateDatabase;

/// <summary>
///     Request to enumerate the selectable image editions (the <c>--wim-index</c> choices) inside an offline image so
///     the Create Database UI can populate its image-index picker. A <c>.wim</c>/<c>.esd</c> is read directly; a
///     <c>.iso</c> is mounted to read its <c>sources\install.wim</c>. Always dispatched through the elevation helper
///     because the ISO mount requires administrator rights, and routing both kinds through one elevated path keeps the
///     behavior uniform.
/// </summary>
/// <param name="ImagePath">Path to the <c>.wim</c>, <c>.esd</c>, or <c>.iso</c> whose editions are listed.</param>
public sealed record ListOfflineImageEditionsRequest(string ImagePath);
