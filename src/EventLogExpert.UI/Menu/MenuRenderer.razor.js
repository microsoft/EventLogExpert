// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

// Used after FocusAsync(preventScroll: true) so keyboard nav still scrolls the focused item
// into view inside its menu panel without jumping the page.
export function scrollMenuItemIntoView(element) {
    if (!element || typeof element.scrollIntoView !== "function") { return; }
    element.scrollIntoView({ block: "nearest", inline: "nearest" });
}