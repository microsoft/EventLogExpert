// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;

namespace EventLogExpert.Runtime.EventLog;

/// <summary>
///     A pending request to scroll the log table to <paramref name="Target" />. <paramref name="WaitForView" />
///     distinguishes the two reveal intents the consumer must treat differently: a wait-for-settle reveal (reload
///     restoration) lingers until the target appears in a rebuilt view, whereas a one-shot selection-driven reveal is
///     discarded the moment the consumer finds the target absent from its current settled view (so a concurrent view
///     change can never strand it).
/// </summary>
public readonly record struct RevealFocusRequest(EventLocator Target, bool WaitForView);
