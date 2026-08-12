// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.FilterPane;

public interface IClearAllFiltersNotifier
{
    event Action Requested;
}
