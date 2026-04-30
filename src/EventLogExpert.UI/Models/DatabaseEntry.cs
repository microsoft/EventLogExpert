// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Models;

public sealed record DatabaseEntry(string FileName, string FullPath, bool IsEnabled, DatabaseStatus Status);
