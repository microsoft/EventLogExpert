// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Eventing.Common.Events;

public readonly record struct ValueKey(long RecordId, DateTime TimeCreated, string OwningLog, string LogName)
{
    /// <summary>
    ///     Builds a key from a resolved event, returning false for a null <c>RecordId</c> (error-read events must not
    ///     merge).
    /// </summary>
    public static bool TryCreate(ResolvedEvent resolvedEvent, out ValueKey key)
    {
        ArgumentNullException.ThrowIfNull(resolvedEvent);

        if (resolvedEvent.RecordId is not { } recordId)
        {
            key = default;

            return false;
        }

        key = new ValueKey(recordId, resolvedEvent.TimeCreated, resolvedEvent.OwningLog, resolvedEvent.LogName);

        return true;
    }
}
