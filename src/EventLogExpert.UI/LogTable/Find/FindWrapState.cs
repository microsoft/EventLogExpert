// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.LogTable.Find;

/// <summary>
///     Whether find navigation wrapped; FindBar maps this to the localized announcement so the component owns the
///     text it renders.
/// </summary>
public enum FindWrapState
{
    None,
    WrappedToFirst,
    WrappedToLast,
}
