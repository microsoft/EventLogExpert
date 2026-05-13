// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Filtering;

public sealed record SubFilter(BasicFilterCondition Data, bool JoinWithAny);
