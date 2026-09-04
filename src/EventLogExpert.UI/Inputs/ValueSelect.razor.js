// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

// A <label for="..."> click focuses the associated input WITHOUT dispatching mousedown on the input,
// so the per-input pointer marker below would miss it and the combobox would wrongly show its
// keyboard ring on a mouse click. One document-level pointerdown listener (registered once) marks the
// ValueSelect input a label points at, so label clicks are correctly treated as pointer focus.
let labelPointerListenerRegistered = false;

function ensureLabelPointerListener() {
    if (labelPointerListenerRegistered) { return; }
    labelPointerListenerRegistered = true;

    document.addEventListener("pointerdown", (e) => {
        const label = e.target && e.target.closest ? e.target.closest("label[for]") : null;
        if (!label) { return; }

        const target = document.getElementById(label.getAttribute("for"));
        // Skip disabled targets: they cannot take focus, so the input's own focus/keydown/blur would
        // never clear the marker, and it would wrongly suppress the keyboard ring once re-enabled.
        if (target && !target.disabled && target.closest(".dropdown-input")) {
            target.setAttribute("data-pointer-focus", "");
        }
    }, true);

    // A pointerdown marker only holds meaning if that pointer interaction actually delivers focus to the
    // input. If it does not (press then drag off the label), the marker would go stale and suppress the
    // ring on a later Tab arrival. Any keydown means the user switched to the keyboard, so clear every
    // lingering marker; the focused input then matches the keyboard-only :focus ring again.
    document.addEventListener("keydown", () => {
        document.querySelectorAll(".dropdown-input [data-pointer-focus]").forEach((el) => {
            el.removeAttribute("data-pointer-focus");
        });
    }, true);
}

export function registerDropdown(root, dotNetRef) {
    const dropdown = root.getElementsByClassName("dropdown-list")[0];
    const input = root.getElementsByTagName("input")[0];
    const controller = new AbortController();

    ensureLabelPointerListener();

    const closeDropdown = (e, force = false) => {
        const target = e.currentTarget.parentNode;

        requestAnimationFrame(() => {
            if (force === false && target.contains(document.activeElement)) { return; }

            dropdown.removeAttribute("data-toggle");
            dropdown.setAttribute("aria-hidden", "true");

            dropdown.style.position = "";
            dropdown.style.top = "";
            dropdown.style.left = "";
            dropdown.style.width = "";

            // Notify C# of JS-driven close. .catch handles disposal race (rejection is async, not sync).
            dotNetRef?.invokeMethodAsync("OnIsOpenChanged", false)?.catch(() => {});
        });
    };

    const scrollToSelected = () => {
        const item = dropdown.querySelector("[aria-selected='true']");
        item?.scrollIntoView({ block: "nearest" });
    };

    const openDropdown = () => {
        const bounds = root.getBoundingClientRect();

        dropdown.style.position = "fixed";
        dropdown.style.top = `${bounds.bottom + 4}px`;
        dropdown.style.left = `${bounds.left}px`;
        dropdown.style.width = `${bounds.width}px`;

        dropdown.setAttribute("data-toggle", "");
        dropdown.setAttribute("aria-hidden", "false");

        // Notify C# of JS-driven open. .catch handles disposal race (rejection is async, not sync).
        dotNetRef?.invokeMethodAsync("OnIsOpenChanged", true)?.catch(() => {});

        scrollToSelected();
    }

    const toggle = (e) => {
        if (dropdown.hasAttribute("data-toggle")) {
            closeDropdown(e, true);
        } else {
            openDropdown();
        }
    };

    input.addEventListener("mousedown", (e) => {
        // Mark pointer-originated focus so the keyboard-only ring (ValueSelect.razor.css) stays off on
        // click. mousedown fires before focus, so the marker is set before :focus matches; it is
        // cleared on keydown/blur.
        input.setAttribute("data-pointer-focus", "");

        e.stopPropagation();

        toggle(e);
    }, { signal: controller.signal });

    input.addEventListener("keydown", (e) => {
        // Keyboard use: allow the keyboard-only focus ring to appear.
        input.removeAttribute("data-pointer-focus");

        // Arrow keys drive dropdown navigation and Enter opens/commits the dropdown, so suppress the
        // browser defaults here: caret-move/scroll for arrows, and Enter submitting an enclosing form
        // (e.g. a select nested in an EditForm). Space opens a select-only (readonly) list, so cancel
        // its native page scroll too; otherwise the captured scroll handler below would close the list
        // this keystroke just opened. Editable (IsInput) controls keep Space so they can type it.
        // Blazor's @onkeydown:preventDefault can't: it reads a field captured at the prior render, so it
        // suppressed the following keystroke instead of this one.
        if (e.code === "ArrowUp" || e.code === "ArrowDown" || e.key === "Enter" || (e.code === "Space" && input.readOnly)) {
            e.preventDefault();
        }
    }, { signal: controller.signal });

    input.addEventListener("blur", (e) => {
        input.removeAttribute("data-pointer-focus");
        closeDropdown(e);
    }, { signal: controller.signal });
    dropdown.addEventListener("blur", (e) => closeDropdown(e), { signal: controller.signal });

    // Native title tooltip, only when the text is actually clipped (scrollWidth > clientWidth): recovers a long
    // selected value or option that overflows the fixed dropdown width, and stays silent when it already fits.
    root.addEventListener("mouseover", (e) => {
        // e.target is an Element for mouseover in practice, but normalize defensively (a Text node has no closest).
        const target = e.target instanceof Element ? e.target : e.target?.parentElement;
        const el = target?.closest("input, [role='option']");
        if (!el || !root.contains(el)) { return; }

        const text = el.tagName === "INPUT" ? el.value : (el.textContent ?? "").trim();

        if (text && el.scrollWidth > el.clientWidth) {
            el.title = text;
        } else if (el.hasAttribute("title")) {
            el.removeAttribute("title");
        }
    }, { signal: controller.signal });

    // The open list is position: fixed and only repositions when it opens, so if a scrollable
    // ancestor (e.g. .page-content) scrolls or the viewport resizes, the combobox can move while the
    // list stays put and detaches. Close the list on those shifts; ignore scrolls that originate
    // inside the list itself so its own options stay scrollable.
    const closeOnViewportShift = (e) => {
        if (!dropdown.hasAttribute("data-toggle")) { return; }
        if (e?.type === "scroll" && dropdown.contains(e.target)) { return; }

        closeDropdown({ currentTarget: input }, true);
    };

    window.addEventListener("scroll", closeOnViewportShift, { capture: true, signal: controller.signal });
    window.addEventListener("resize", closeOnViewportShift, { signal: controller.signal });

    root._dropdownController = controller;
}

export function unregisterDropdown(root) {
    root?._dropdownController?.abort();
}

export function closeDropdown(root) {
    const dropdown = root.getElementsByClassName("dropdown-list")[0];

    dropdown.removeAttribute("data-toggle");
    dropdown.setAttribute("aria-hidden", "true");

    dropdown.style.position = "";
    dropdown.style.top = "";
    dropdown.style.left = "";
    dropdown.style.width = "";
}

export function openDropdown(root) {
    const dropdown = root.getElementsByClassName("dropdown-list")[0];
    const bounds = root.getBoundingClientRect();

    if (dropdown.hasAttribute("data-toggle")) { return; }

    dropdown.style.position = "fixed";
    dropdown.style.top = `${bounds.bottom + 4}px`;
    dropdown.style.left = `${bounds.left}px`;
    dropdown.style.width = `${bounds.width}px`;

    dropdown.setAttribute("data-toggle", "");
    dropdown.setAttribute("aria-hidden", "false");

    scrollToSelectedItem(root);
}

export function scrollToHighlightedItem(root) {
    const dropdown = root.getElementsByClassName("dropdown-list")[0];
    const item = dropdown.querySelector("[highlighted]");
    item?.scrollIntoView({ block: "nearest" });
}

export function scrollToSelectedItem(root) {
    const dropdown = root.getElementsByClassName("dropdown-list")[0];
    const item = dropdown.querySelector("[aria-selected='true']");
    item?.scrollIntoView({ block: "nearest" });
}

export function toggleDropdown(root) {
    const dropdown = root.getElementsByClassName("dropdown-list")[0];

    if (dropdown.hasAttribute("data-toggle")) {
        closeDropdown(root);
    } else {
        openDropdown(root);
    }
}