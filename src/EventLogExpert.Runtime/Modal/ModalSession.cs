// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Modal;

/// <summary>Snapshot of the active modal as observed by <see cref="IModalCoordinator" />.</summary>
public sealed record ModalSession(ModalId Id, Type ComponentType, IDictionary<string, object?>? Parameters)
{
    public bool Equals(ModalSession? other) => other is not null && Id == other.Id;

    public override int GetHashCode() => Id.GetHashCode();
}
