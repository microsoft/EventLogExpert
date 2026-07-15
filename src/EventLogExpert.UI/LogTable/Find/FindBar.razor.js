// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

// Cancel WebView2's native F3 find accelerator in the capture phase; the Blazor @onkeydown handler drives navigation on the bubble phase.
let suppressor = null;

export function attachNativeFindSuppression() {
    if (suppressor) {
        return;
    }

    suppressor = (e) => {
        if (e.key === "F3") {
            e.preventDefault();
        }
    };

    document.addEventListener("keydown", suppressor, true);
}

export function detachNativeFindSuppression() {
    if (suppressor) {
        document.removeEventListener("keydown", suppressor, true);
        suppressor = null;
    }
}

export function focusAndSelect(element) {
    if (!element) {
        return;
    }

    element.focus();

    if (typeof element.select === "function") {
        element.select();
    }
}
