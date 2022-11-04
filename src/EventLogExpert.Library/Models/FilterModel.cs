// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Library.Models;

public class FilterModel
{
    public FilterModel(int id) => Id = id;

    public int Id { get; set; }

    public Func<DisplayEventModel, bool>? Comparison { get; set; }
}
