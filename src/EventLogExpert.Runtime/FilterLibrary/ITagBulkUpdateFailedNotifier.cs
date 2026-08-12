// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.FilterLibrary;

public interface ITagBulkUpdateFailedNotifier
{
    event Action Failed;
}
