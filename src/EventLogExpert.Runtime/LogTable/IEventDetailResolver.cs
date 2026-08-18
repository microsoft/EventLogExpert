// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using System.Diagnostics.CodeAnalysis;

namespace EventLogExpert.Runtime.LogTable;

public interface IEventDetailResolver
{
    bool TryResolve(EventLocator locator, [NotNullWhen(true)] out ResolvedEvent? detail);

    /// <summary>
    ///     Like <see cref="TryResolve" /> but rehydrates only the grid-visible fields (Description/Level/Source/Id; no
    ///     XML, EventData, or UserData), for lightweight list rendering.
    /// </summary>
    bool TryResolveLean(EventLocator locator, [NotNullWhen(true)] out ResolvedEvent? detail);
}
