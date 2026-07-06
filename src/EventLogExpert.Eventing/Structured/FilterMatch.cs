// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Eventing.Structured;

public enum FilterMatch : byte
{
    NoMatch = 0,
    Match = 1,
    Unknown = 2
}
