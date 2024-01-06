// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Models;

public sealed record FilterGroupModel
{
    public Guid Id { get; } = Guid.NewGuid();

    public string Name { get; set; } = "New Filter Group";

    public IEnumerable<FilterModel> Filters { get; set; } = [];
}
