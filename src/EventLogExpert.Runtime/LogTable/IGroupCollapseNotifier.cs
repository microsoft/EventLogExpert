// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.LogTable;

public interface IGroupCollapseNotifier
{
    event Action Requested;
}
