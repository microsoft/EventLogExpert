// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

export function registerDropdown(root, dotNetRef) {
    const dropdown = root.getElementsByClassName("dropdown-list")[0];
    const input = root.getElementsByTagName("input")[0];
    const controller = new AbortController();

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
        e.stopPropagation();

        toggle(e);
    }, { signal: controller.signal });

    input.addEventListener("keydown", (e) => {
        // Arrow keys drive dropdown navigation, so suppress the browser's caret-move/scroll default here.
        // Blazor's @onkeydown:preventDefault can't: it reads a field captured at the prior render, so it
        // suppressed the following keystroke instead of this one.
        if (e.code === "ArrowUp" || e.code === "ArrowDown") {
            e.preventDefault();
        }
    }, { signal: controller.signal });

    input.addEventListener("blur", (e) => closeDropdown(e), { signal: controller.signal });
    dropdown.addEventListener("blur", (e) => closeDropdown(e), { signal: controller.signal });

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