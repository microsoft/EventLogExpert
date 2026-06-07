// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

// Scroll a referenced element into the nearest scrollable ancestor so expanding inline panels
// (e.g., tag editor under a library row) don't get clipped by modal body overflow.
export function scrollElementIntoView(element) {
    if (element && typeof element.scrollIntoView === "function") {
        const reduceMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
        element.scrollIntoView({ block: "nearest", behavior: reduceMotion ? "auto" : "smooth" });
    }
}