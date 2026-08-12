// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using System.Diagnostics.CodeAnalysis;

namespace EventLogExpert.Runtime.LogTable;

public interface IEventDetailResolver
{
    bool TryResolve(EventLocator locator, [NotNullWhen(true)] out ResolvedEvent? detail);
}
