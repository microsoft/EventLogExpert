// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.Shared.Base;

/// <summary>
/// Generic base for filter rows. Holds the minimal common contract: a stable DOM <see cref="Id"/>
/// for aria labelling and the typed <see cref="Value"/> the row renders. Concrete rows that bind
/// to a saved <see cref="FilterModel"/> should inherit <see cref="EditableFilterRowBase"/> to
/// also pick up the edit lifecycle; rows that render a draft node (e.g. <c>SubFilterRow</c>)
/// inherit this base directly with <typeparamref name="TValue"/> = <see cref="FilterEditorModel"/>.
/// </summary>
public abstract class FilterRowBase<TValue> : ComponentBase
{
    [Parameter] public string Id { get; set; } = Guid.NewGuid().ToString();

    [Parameter] public TValue Value { get; set; } = default!;
}