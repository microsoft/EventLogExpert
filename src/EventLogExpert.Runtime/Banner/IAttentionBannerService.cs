// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Database;

namespace EventLogExpert.Runtime.Banner;

public interface IAttentionBannerService
{
    event Action StateChanged;

    bool AttentionDismissed { get; }

    IReadOnlyList<DatabaseEntry> AttentionEntries { get; }

    void DismissAttention();
}
