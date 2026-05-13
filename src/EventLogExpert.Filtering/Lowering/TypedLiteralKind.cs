// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Filtering.Lowering;

/// <summary>
///     Discriminator for <see cref="TypedLiteral" /> identifying which slot in the union carries the parsed value.
///     Coercion happens once at lower-time (per N-D6) so the per-event hot path performs a raw typed compare.
/// </summary>
internal enum TypedLiteralKind
{
    String,
    Int,
    Long,
    Guid,
    Null
}
