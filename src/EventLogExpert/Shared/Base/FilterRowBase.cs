// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Microsoft.AspNetCore.Components;

namespace EventLogExpert.Shared.Base;

/// <summary>Generic base for filter rows: stable DOM <see cref="Id" /> and a typed <see cref="Value" />.</summary>
public abstract class FilterRowBase<TValue> : ComponentBase
{
    [Parameter] public string Id { get; set; } = Guid.NewGuid().ToString();

    [Parameter] public TValue Value { get; set; } = default!;
}
