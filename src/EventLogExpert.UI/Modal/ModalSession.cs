// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Modal;

public sealed record ModalSession(ModalId Id, Type ComponentType, IDictionary<string, object?>? Parameters)
{
    public bool Equals(ModalSession? other) => other is not null && Id == other.Id;

    public override int GetHashCode() => Id.GetHashCode();
}
