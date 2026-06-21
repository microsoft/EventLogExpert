// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Provider.Resolution;

public interface ILazyMessageSource
{
    int Count { get; }

    IReadOnlyList<MessageModel> AsView();

    MessageModel? GetByRawIdFirst(long rawId);

    IReadOnlyList<MessageModel> GetByShortId(int shortId);

    IReadOnlyList<MessageModel> MaterializeAll();
}
