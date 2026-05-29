// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Banner;

namespace EventLogExpert.UI.Banner;

public interface IBannerCycleStateService
{
    event Action? StateChanged;

    BannerView CurrentView { get; }

    int DisplayedIndex { get; }

    IReadOnlyList<BannerCycleItem> Items { get; }

    bool ModalContentDisplayed { get; }

    BannerCycleItem? SelectedItem { get; }

    void MoveNext();

    void MovePrev();

    void RegisterFallbackError(BannerCycleItem newCycleItem);

    void SetModalContentDisplayed(bool displayed);
}
